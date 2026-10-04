using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the declarative-facing PLAINTORCH application API (PEP100).
/// </summary>
/// <remarks>
/// Declaratives are event-like: they are never acted on directly. Interacting with a single occurrence
/// addresses it by its RECURRENCE-ID (an eventive for fates/objectives, an unbound attentive for decrees);
/// the occurrence is resolved into the same unit of work and the interaction applied, so a projected occurrence
/// hardens as the guaranteed consequence of the single save rather than through a separate materialize verb.
/// Instances resolved here are never Polaris-bound; binding only happens by manually adding a decree to a
/// Polaris cycle through the Polaris API, and a bound attentive — having no meaningful recurrence-id — is
/// addressed by its row id instead. Interaction with an orbit-scheduled occurrence is resolved through preview
/// (non-seeking) resolution so a future instance never pushes the schedule state forward; its recurrence-id is
/// its identity, so the later seeking pass recognizes it. A manual eventive — added to a fate by hand, outside its
/// recurrence set — has no RECURRENCE-ID; it is born hardened and addressed by its row id.
/// </remarks>
public sealed class DeclarativeApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	VaultWriteQueue writeQueue,
	VaultTemporalDataService temporalDataService,
	DependencyGateService dependencyGate,
	OccurrenceHardeningService hardeningService,
	PlaintorchOrbitService orbitService,
	VaultEntityLifecycleService lifecycleService,
	VaultAuditLogService auditLogService) : IDeclarativeApi
{
	/// <inheritdoc />
	public Task<Fate?> GetFateAsync(string fateId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		return context.Fates
			.AsNoTracking()
			.FirstOrDefaultAsync(fate => fate.Id == fateId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Fate>> ListFatesAsync(CancellationToken cancellationToken = default)
	{
		return await context.Fates
			.AsNoTracking()
			.OrderBy(fate => fate.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Fate> CreateFateAsync(FatePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
		await EnsureDirectiveExistsAsync(plan.DirectiveId, cancellationToken);
		ValidateEventWindow(plan.StartTime, plan.EndTime);

		// A fate is stored orbit-only (PEP111). An explicit orbit wins; otherwise a one-off plan (a date, optionally
		// timed) folds into a fixed-datetime Z{…} literal so the fate is never born carrying two scheduling shapes.
		var orbit = !string.IsNullOrWhiteSpace(plan.Orbit)
			? plan.Orbit
			: ComposeOneOffOrbit(plan.Date, plan.StartTime, plan.EndTime, plan.EventDuration);
		PlaintorchOrbitService.ValidateFateOrbit(orbit);

		var fate = new Fate
		{
			Id = puckCreationService.CreateIdFor<Fate>(plan.Id),
			Title = plan.Title,
			DirectiveId = plan.DirectiveId,
			Orbit = orbit,
			// A one-off Z{…} literal is Gregorian-authored; pin it so the global default calendar never reinterprets it.
			Calendar = PinnedCalendarFor(orbit),
		};

		if (!string.IsNullOrWhiteSpace(plan.ParentIncentiveId))
		{
			await ApplyParentAsync(fate, plan.ParentIncentiveId, cancellationToken);
		}

		context.Fates.Add(fate);
		await writeQueue.WriteAsync(fate, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "fate.create", subject: fate, cancellationToken: cancellationToken);
		return fate;
	}

	/// <inheritdoc />
	public async Task<Fate> UpdateFateAsync(string fateId, FateUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		ArgumentNullException.ThrowIfNull(update);

		var fate = await context.Fates.FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");
		var previous = CloneFate(fate);

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			fate.Title = update.Title;
		}

		if (update.Status is not null && update.Status.Value != fate.Status)
		{
			await dependencyGate.EnsureCanTransitionAsync(new EndpointRef(DependencyEndpointKind.Fate, fate.Id), update.Status.Value, cancellationToken);
			fate.Status = update.Status.Value;
		}

		if (update.DirectiveId.IsSet)
		{
			// A present directive is applied: a value moves the fate under it, an explicit null makes it standalone.
			var directiveId = string.IsNullOrWhiteSpace(update.DirectiveId.Value) ? null : update.DirectiveId.Value;
			await EnsureDirectiveExistsAsync(directiveId, cancellationToken);
			fate.DirectiveId = directiveId;
		}

		if (update.ParentIncentiveId.IsSet)
		{
			if (string.IsNullOrWhiteSpace(update.ParentIncentiveId.Value))
			{
				fate.ParentIncentiveId = null;
			}
			else
			{
				await ApplyParentAsync(fate, update.ParentIncentiveId.Value, cancellationToken);
			}
		}

		if (update.Orbit is not null)
		{
			// A fate is orbit-only (PEP111): rescheduling — a one-off Z{…} or a recurrence — is a new orbit, and an
			// empty orbit unschedules the fate.
			var normalizedOrbit = string.IsNullOrWhiteSpace(update.Orbit) ? null : update.Orbit;
			PlaintorchOrbitService.ValidateFateOrbit(normalizedOrbit);
			var previousOrbit = fate.Orbit;
			fate.Orbit = normalizedOrbit;
			// A calendar sent alongside is the one the new orbit is written in. Without one, a one-off literal must still
			// be read on a definite calendar: Gregorian, as a client naming none writes it (PEP111).
			if (!update.Calendar.IsSet)
			{
				fate.Calendar = CalendarForNewOrbit(fate.Calendar, previousOrbit, normalizedOrbit);
			}
		}

		if (update.Calendar.IsSet)
		{
			fate.Orbit = ApplyCalendar(fate, fate.Orbit, update.Calendar.Value, rewriteOneOff: update.Orbit is null);
		}

		if (update.TimeZone.IsSet)
		{
			fate.TimeZone = ValidTimeZone(update.TimeZone.Value, nameof(update));
		}

		// Resuming a paused fate (cancelled or opted out → active) seeks its cursor to now, so generation picks up
		// from now with no backfill of the occurrences that elapsed while it was paused (PEP100/PEP111).
		if (previous.Status != FateStatus.Active && fate.Status == FateStatus.Active && !string.IsNullOrWhiteSpace(fate.Orbit))
		{
			await orbitService.FastForwardToNowAsync(fate, fate.Orbit!, DateTimeOffset.Now, cancellationToken);
		}

		// A fate is orbit-only (PEP111), so there is no fixed-date shape to reconcile here — the update above
		// already set the orbit (and re-pinned the calendar). The write goes through the vault write queue (PEP110).
		await writeQueue.WriteAsync(fate, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "fate.update", subject: fate, cancellationToken: cancellationToken);
		return fate;
	}

	/// <inheritdoc />
	public async Task<Fate> BeginFateBoundaryAsync(string fateId, CancellationToken cancellationToken = default)
		=> (Fate)await lifecycleService.BeginBoundaryAsync(typeof(Fate), fateId, cancellationToken);

	/// <inheritdoc />
	public async Task DeleteFateAsync(string fateId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		var fate = await context.Fates
			.Include(item => item.ChildIncentives)
			.FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");

		if (fate.ChildIncentives.Count > 0)
		{
			throw new InvalidOperationException("Fate cannot be deleted while other incentives still name it as their parent.");
		}

		var graveyardEntry = temporalDataService.StageEntityArchive(fate, "api-delete", Environment.UserName);
		context.Fates.Remove(fate);
		await writeQueue.RecordRemoveAsync(fate, cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		await writeQueue.DrainRemoveAsync(fate, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"fate.delete",
			subjectType: nameof(Fate),
			subjectId: fate.Id,
			subjectTitle: fate.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public Task<Decree?> GetDecreeAsync(string decreeId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		return context.Decrees
			.AsNoTracking()
			.FirstOrDefaultAsync(decree => decree.Id == decreeId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Decree>> ListDecreesAsync(CancellationToken cancellationToken = default)
	{
		return await context.Decrees
			.AsNoTracking()
			.OrderBy(decree => decree.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Decree> CreateDecreeAsync(DecreePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
		await EnsureDirectiveExistsAsync(plan.DirectiveId, cancellationToken);
		PlaintorchOrbitService.ValidateDecreeOrbit(plan.Orbit, plan.Reflect);

		var decree = new Decree
		{
			Id = puckCreationService.CreateIdFor<Decree>(plan.Id),
			Title = plan.Title,
			DirectiveId = plan.DirectiveId,
			Orbit = plan.Orbit,
			DefaultLength = plan.DefaultLength,
			ActiveCelestron = plan.ActiveCelestron,
			Reflect = plan.Reflect,
		};

		context.Decrees.Add(decree);
		await writeQueue.WriteAsync(decree, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "decree.create", subject: decree, cancellationToken: cancellationToken);
		return decree;
	}

	/// <inheritdoc />
	public async Task<Decree> UpdateDecreeAsync(string decreeId, DecreeUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		ArgumentNullException.ThrowIfNull(update);

		var decree = await context.Decrees.FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");
		var previous = CloneDecree(decree);

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			decree.Title = update.Title;
		}

		if (update.Status is not null)
		{
			decree.Status = update.Status.Value;
		}

		if (update.DirectiveId.IsSet)
		{
			// A present directive is applied: a value moves the decree under it, an explicit null makes it standalone.
			var directiveId = string.IsNullOrWhiteSpace(update.DirectiveId.Value) ? null : update.DirectiveId.Value;
			await EnsureDirectiveExistsAsync(directiveId, cancellationToken);
			decree.DirectiveId = directiveId;
		}

		if (update.Orbit is not null)
		{
			decree.Orbit = string.IsNullOrWhiteSpace(update.Orbit) ? null : update.Orbit;
		}

		if (update.DefaultLength is not null)
		{
			decree.DefaultLength = update.DefaultLength;
		}

		if (update.ActiveCelestron is not null)
		{
			decree.ActiveCelestron = update.ActiveCelestron.Value;
		}

		if (update.Reflect is not null)
		{
			decree.Reflect = update.Reflect.Value;
		}

		if (update.Calendar.IsSet)
		{
			decree.Orbit = ApplyCalendar(decree, decree.Orbit, update.Calendar.Value, rewriteOneOff: update.Orbit is null);
		}

		if (update.TimeZone.IsSet)
		{
			decree.TimeZone = ValidTimeZone(update.TimeZone.Value, nameof(update));
		}

		// Validate the resulting combination: reflecting decrees demand day-granularity orbits.
		PlaintorchOrbitService.ValidateDecreeOrbit(decree.Orbit, decree.Reflect);

		// Resuming an abandoned decree (→ active) seeks its cursor to now, so generation picks up from now with no
		// backfill of the occurrences that elapsed while it was abandoned (PEP100/PEP111).
		if (previous.Status != DecreeStatus.Active && decree.Status == DecreeStatus.Active && !string.IsNullOrWhiteSpace(decree.Orbit))
		{
			await orbitService.FastForwardToNowAsync(decree, decree.Orbit!, DateTimeOffset.Now, cancellationToken);
		}

		await writeQueue.WriteAsync(decree, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "decree.update", subject: decree, cancellationToken: cancellationToken);
		return decree;
	}

	/// <inheritdoc />
	public async Task<Decree> BeginDecreeBoundaryAsync(string decreeId, CancellationToken cancellationToken = default)
		=> (Decree)await lifecycleService.BeginBoundaryAsync(typeof(Decree), decreeId, cancellationToken);

	/// <inheritdoc />
	public async Task DeleteDecreeAsync(string decreeId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		var decree = await context.Decrees.FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");

		var graveyardEntry = temporalDataService.StageEntityArchive(decree, "api-delete", Environment.UserName);
		context.Decrees.Remove(decree);
		await writeQueue.RecordRemoveAsync(decree, cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		await writeQueue.DrainRemoveAsync(decree, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"decree.delete",
			subjectType: nameof(Decree),
			subjectId: decree.Id,
			subjectTitle: decree.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Eventive>> ListEventivesAsync(string? fateId = null, string? objectiveId = null, CancellationToken cancellationToken = default)
	{
		var query = context.Eventives.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(fateId))
		{
			query = query.Where(item => item.FateId == fateId);
		}

		if (!string.IsNullOrWhiteSpace(objectiveId))
		{
			query = query.Where(item => item.ObjectiveId == objectiveId);
		}

		return await query
			.OrderBy(item => item.Epoch.Moment)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Attentive>> ListAttentivesAsync(string? decreeId = null, CancellationToken cancellationToken = default)
	{
		var query = context.Attentives.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(decreeId))
		{
			query = query.Where(item => item.DecreeId == decreeId);
		}

		return await query
			.OrderBy(item => item.Epoch.Moment)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Eventive> CreateEventiveAsync(EventivePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.FateId);
		var epoch = GivenEpoch(plan.Epoch);

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == plan.FateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{plan.FateId}' was not found.");
		// A cancelled fate takes no occurrences, as it materializes none (PEP100). An opted-out fate still accepts one:
		// adding it by hand is the explicit interaction that opts that single occurrence in.
		if (fate.Status == FateStatus.Cancelled)
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' is {fate.Status} and takes no eventives.");
		}

		// PEP101: a locked whole-fate freezes all materialization. A manual occurrence has no slot, so no single
		// occurrence lock can name it.
		if (await dependencyGate.IsFateMaterializationBlockedAsync(fate.Id, recurrenceId: null, cancellationToken))
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' is blocked by unmet dependencies and cannot take an eventive.");
		}

		var eventive = new Eventive
		{
			FateId = fate.Id,
			Epoch = epoch,
			RecurrenceId = null,
			Resolution = EventiveResolution.Pending,
		};
		context.Eventives.Add(eventive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"eventive.create",
			subjectType: nameof(Eventive),
			subjectId: eventive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { eventive.FateId, manual = true, date = eventive.Epoch.Date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return eventive;
	}

	/// <inheritdoc />
	public async Task<Eventive> UpdateEventiveAsync(EventiveOccurrenceRef occurrence, EventiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(occurrence);
		ArgumentNullException.ThrowIfNull(update);

		var eventive = await ResolveEventiveAsync(occurrence, cancellationToken);

		// Eventives are never Polaris-bound, so moving their time specification is always allowed. Moving is a
		// change to the occurrence's Epoch: a whole epoch replaces it, then a date shifts the moment's day, a start
		// time shifts its time of day (and its granularity), and an end time becomes the nominal duration from the start.
		if (update.Epoch is not null)
		{
			Reschedule(eventive.Epoch, GivenEpoch(update.Epoch));
		}

		if (update.Date is not null)
		{
			eventive.Epoch.Moment = update.Date.Value.ToDateTime(TimeOnly.FromDateTime(eventive.Epoch.Moment));
		}

		if (update.StartTime.IsSet)
		{
			eventive.Epoch.Moment = eventive.Epoch.Date.ToDateTime(update.StartTime.Value ?? TimeOnly.MinValue);
			eventive.Epoch.Granularity = update.StartTime.Value is null ? OrbitUnit.Day : OrbitUnit.Minute;
		}

		if (update.EndTime.IsSet)
		{
			eventive.Epoch.Duration = OccurrenceDurations.Format(OccurrenceDurations.SpanMinutes(eventive.Epoch.TimeOfDay, update.EndTime.Value));
		}

		if (update.Resolution is not null)
		{
			eventive.Resolution = update.Resolution.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"eventive.update",
			subjectType: nameof(Eventive),
			subjectId: eventive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { eventive.FateId, eventive.ObjectiveId, resolution = eventive.Resolution.ToString(), date = eventive.Epoch.Date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return eventive;
	}

	/// <inheritdoc />
	public async Task<Attentive> UpdateAttentiveAsync(AttentiveOccurrenceRef occurrence, AttentiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(occurrence);
		ArgumentNullException.ThrowIfNull(update);

		// An attentive is always an unbound occurrence, addressed by its decree + recurrence-id. Resolve it into the
		// current unit of work without a save of its own; the single SaveChanges below persists the materialization
		// together with this interaction, and the state-policy pass runs over it centrally.
		if (string.IsNullOrWhiteSpace(occurrence.DecreeId) || occurrence.RecurrenceId is not DateTime recurrenceId)
		{
			throw new ArgumentException("An attentive occurrence must be addressed by decree and recurrence-id.", nameof(occurrence));
		}

		var attentive = await hardeningService.EnsureDecreeAttentiveIntoContextAsync(occurrence.DecreeId, recurrenceId, cancellationToken);

		if (update.Epoch is not null)
		{
			Reschedule(attentive.Epoch, GivenEpoch(update.Epoch));
		}

		if (update.Date is not null)
		{
			attentive.Epoch.Moment = update.Date.Value.ToDateTime(TimeOnly.FromDateTime(attentive.Epoch.Moment));
		}

		if (update.Time.IsSet)
		{
			attentive.Epoch.Moment = attentive.Epoch.Date.ToDateTime(update.Time.Value ?? TimeOnly.MinValue);
			attentive.Epoch.Granularity = update.Time.Value is null ? OrbitUnit.Day : OrbitUnit.Minute;
		}

		if (update.Resolution is not null)
		{
			attentive.Resolution = update.Resolution.Value;
		}

		if (update.AffinityTimeframeId.IsSet)
		{
			if (update.AffinityTimeframeId.Value is long timeframeId
				&& !await context.Timeframes.AnyAsync(item => item.Id == timeframeId, cancellationToken))
			{
				throw new InvalidOperationException($"Timeframe '{timeframeId}' was not found.");
			}

			attentive.AffinityTimeframeId = update.AffinityTimeframeId.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"attentive.update",
			subjectType: nameof(Attentive),
			subjectId: attentive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { attentive.DecreeId, resolution = attentive.Resolution.ToString(), date = attentive.Epoch.Date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return attentive;
	}

	/// <summary>
	/// A time zone as a declarative or an epoch stores it: trimmed, <see langword="null"/> for none (floating), and
	/// refused when this host does not know it as an IANA (or system) zone.
	/// </summary>
	private static string? ValidTimeZone(string? requested, string parameterName)
	{
		var timeZone = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
		if (timeZone is not null && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
		{
			throw new ArgumentException($"'{timeZone}' is not a known time zone.", parameterName);
		}

		return timeZone;
	}

	/// <summary>
	/// The calendar a fate keeps when its orbit is replaced and the update names none: a one-off literal is read on
	/// the fate's own calendar, else pinned to Gregorian (the calendar a client naming none writes it in); a recurrence
	/// replacing such a pinned one-off returns to the vault default, as that pin served only the literal (PEP111).
	/// Any other calendar — one the user chose — is kept.
	/// </summary>
	private static DeclarativeCalendar? CalendarForNewOrbit(DeclarativeCalendar? current, string? previousOrbit, string? orbit)
	{
		if (IsOneOffLiteral(orbit))
		{
			return current ?? DeclarativeCalendar.Gregorian;
		}

		return IsOneOffLiteral(previousOrbit) && current == DeclarativeCalendar.Gregorian ? null : current;
	}

	/// <summary>
	/// Sets a declarative's calendar and returns the orbit it keeps. A lone one-off literal names a day on the calendar
	/// it is read in, so when the calendar actually changes (<paramref name="rewriteOneOff"/>, and the resolved calendar
	/// moves) the literal is rewritten into the new one and keeps its day; a recurrence only resolves on the new
	/// calendar from here on. A literal the new calendar cannot write is refused rather than left naming another day.
	/// </summary>
	private string? ApplyCalendar(Declarative declarative, string? orbit, DeclarativeCalendar? calendar, bool rewriteOneOff)
	{
		var from = orbitService.ResolveCalendar(declarative);
		declarative.Calendar = calendar;
		var to = orbitService.ResolveCalendar(declarative);
		if (!rewriteOneOff || ReferenceEquals(from, to) || orbit is null || OrbitDays.FixedLiteralDate(orbit, from) is null)
		{
			return orbit;
		}

		return OrbitDays.RewriteFixedLiteral(orbit, from, to)
			?? throw new InvalidOperationException($"'{orbit}' names a day the {calendar?.ToString() ?? "default"} calendar cannot write as a one-off date.");
	}

	private static bool IsOneOffLiteral(string? orbit)
		=> !string.IsNullOrWhiteSpace(orbit) && orbit.TrimStart().StartsWith("Z{", StringComparison.Ordinal);

	/// <summary>
	/// Validates a position in time a client gives an occurrence (a manual eventive's on creation, or any occurrence's
	/// when it is rescheduled whole) and copies it into a fresh <see cref="Epoch"/>: the granularity must be a known
	/// unit, the duration well-formed Orbit duration notation (or absent, filling one granularity unit), and the time
	/// zone one this host knows (or absent, floating). A day-or-finer moment is cut down to the start of its unit, as an
	/// orbit occurrence's is; a week, month or year moment is kept as given, since where those begin depends on the
	/// calendar it was chosen on.
	/// </summary>
	private static Epoch GivenEpoch(Epoch? requested)
	{
		ArgumentNullException.ThrowIfNull(requested, "epoch");
		if (!Enum.IsDefined(requested.Granularity))
		{
			throw new ArgumentException($"'{requested.Granularity}' is not a granularity.", "epoch");
		}

		var duration = string.IsNullOrWhiteSpace(requested.Duration) ? null : requested.Duration.Trim();
		if (duration is not null && !OccurrenceDurations.IsValid(duration))
		{
			throw new ArgumentException($"'{duration}' is not an Orbit duration (such as 90m, 1d6h or 1M).", "epoch");
		}

		var timeZone = ValidTimeZone(requested.TimeZone, "epoch");

		var moment = requested.Moment;
		var start = requested.Granularity switch
		{
			OrbitUnit.Day => moment.Date,
			OrbitUnit.Hour => moment.Date.AddHours(moment.Hour),
			OrbitUnit.Minute => moment.Date.AddHours(moment.Hour).AddMinutes(moment.Minute),
			OrbitUnit.Second => moment.Date.AddHours(moment.Hour).AddMinutes(moment.Minute).AddSeconds(moment.Second),
			_ => moment,
		};

		return new Epoch
		{
			// Civil (wall-clock) time: whatever kind the wire gave it, the stored moment carries no zone of its own.
			Moment = DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
			Granularity = requested.Granularity,
			Duration = duration,
			TimeZone = timeZone,
		};
	}

	/// <summary>
	/// Moves an occurrence to a validated position in time (<see cref="GivenEpoch"/>), member by member, so the epoch it
	/// owns stays the one tracked. Its RECURRENCE-ID is left alone: that is the slot it was born on, its identity.
	/// </summary>
	private static void Reschedule(Epoch epoch, Epoch to)
	{
		epoch.Moment = to.Moment;
		epoch.Granularity = to.Granularity;
		epoch.Duration = to.Duration;
		epoch.TimeZone = to.TimeZone;
	}

	/// <summary>
	/// Resolves the eventive an occurrence reference addresses into the current unit of work, without a save of its
	/// own. A row id names a hardened row directly — the only address a manual eventive has. Otherwise the owner and
	/// recurrence-id resolve it through the hardening service (evaluate the orbit, find the hardened twin, or build
	/// the projected row), which addresses a projected occurrence and its hardened twin identically; the caller's
	/// single SaveChanges then persists the materialization together with the interaction, and the state-policy pass
	/// runs over it centrally.
	/// </summary>
	private async Task<Eventive> ResolveEventiveAsync(EventiveOccurrenceRef occurrence, CancellationToken cancellationToken)
	{
		if (occurrence.Id is long id)
		{
			return await context.Eventives.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
				?? throw new InvalidOperationException($"Eventive '{id}' was not found.");
		}

		if (string.IsNullOrWhiteSpace(occurrence.OwnerId) || occurrence.RecurrenceId is not DateTime recurrenceId)
		{
			throw new ArgumentException("An eventive occurrence must be addressed by its row id, or by owner and recurrence-id.", nameof(occurrence));
		}

		return await hardeningService.EnsureEventiveIntoContextAsync(occurrence.OwnerId, recurrenceId, cancellationToken);
	}

	private async Task ApplyParentAsync(Incentive child, string parentIncentiveId, CancellationToken cancellationToken)
	{
		var parent = await context.Incentives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.FirstOrDefaultAsync(item => item.Id == parentIncentiveId, cancellationToken)
			?? throw new InvalidOperationException($"Parent incentive '{parentIncentiveId}' was not found.");

		IncentiveParenting.EnsureValidParent(child, parent);
		child.ParentIncentiveId = parent.Id;
	}

	private async Task EnsureDirectiveExistsAsync(string? directiveId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(directiveId))
		{
			return;
		}

		var exists = await context.Directives.AnyAsync(item => item.Id == directiveId, cancellationToken);
		if (!exists)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		}
	}

	private static Fate CloneFate(Fate fate)
	{
		return new Fate
		{
			Id = fate.Id,
			Title = fate.Title,
			DirectiveId = fate.DirectiveId,
			ParentIncentiveId = fate.ParentIncentiveId,
			Status = fate.Status,
			Orbit = fate.Orbit,
		};
	}

	/// <summary>
	/// Folds a one-off fate plan (a date, optionally timed) into a fixed-datetime Orbit literal (PEP111): a
	/// date-only <c>Z{y/M/d}</c> for an all-day occurrence, or a <c>Z{y/M/dTh:m}</c> down to the minute with a
	/// <c>=&lt;dur&gt;</c> span when the event has a window (its end time, else its event duration). Returns
	/// <see langword="null"/> when there is no date to schedule.
	/// </summary>
	private static string? ComposeOneOffOrbit(DateOnly? date, TimeOnly? startTime, TimeOnly? endTime, int? eventDuration)
	{
		if (date is not { } day)
		{
			return null;
		}

		if (startTime is not { } start)
		{
			return $"Z{{{day.Year}/{day.Month}/{day.Day}}}";
		}

		var head = $"Z{{{day.Year}/{day.Month}/{day.Day}T{start.Hour:D2}:{start.Minute:D2}}}";
		var span = OccurrenceDurations.SpanMinutes(start, endTime) ?? (eventDuration > 0 ? eventDuration : null);
		var duration = OccurrenceDurations.Format(span);
		return duration is null ? head : $"{head}={duration}";
	}

	/// <summary>
	/// The calendar a fate pins itself to given its orbit (PEP111): a fixed-datetime <c>Z{…}</c> literal is an
	/// absolute civil moment authored in Gregorian, so it pins <see cref="DeclarativeCalendar.Gregorian"/> and is
	/// never reinterpreted by the vault's global default calendar (which may be Pleiadean, where its month/day
	/// would overflow a six-month year). A recurrence follows the global default (<see langword="null"/>).
	/// </summary>
	private static DeclarativeCalendar? PinnedCalendarFor(string? orbit)
		=> !string.IsNullOrWhiteSpace(orbit) && orbit.TrimStart().StartsWith("Z{", System.StringComparison.Ordinal)
			? DeclarativeCalendar.Gregorian
			: null;

	private static Decree CloneDecree(Decree decree)
	{
		return new Decree
		{
			Id = decree.Id,
			Title = decree.Title,
			DirectiveId = decree.DirectiveId,
			Status = decree.Status,
			Orbit = decree.Orbit,
			DefaultLength = decree.DefaultLength,
			ActiveCelestron = decree.ActiveCelestron,
			Reflect = decree.Reflect,
		};
	}

	private static void ValidateEventWindow(TimeOnly? startTime, TimeOnly? endTime)
	{
		if (startTime is null && endTime is not null)
		{
			throw new ArgumentException("A fate cannot carry an end time without a start time.");
		}

		if (startTime is not null && endTime is not null && endTime < startTime)
		{
			throw new ArgumentException("Fate end time cannot be earlier than its start time.");
		}
	}
}
