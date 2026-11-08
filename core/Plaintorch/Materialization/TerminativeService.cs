using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The one due mechanism (PEP103 patch 7): how every item under the due contract (<see cref="IDueOwner"/>) spawns and keeps
/// its <see cref="Terminative"/>. The projection, the rolling pass, interaction and the state engine all go through here,
/// so each kind that carries a due is read one way: whether its due acts, which items' dues fall in a window, and how a
/// terminative mirrors its origin.
/// </summary>
/// <remarks>
/// Every method takes the unit of work it reads, so the state engine reads the context it is saving rather than the scope's.
/// Kind-specific code is confined to the store queries, since each kind keeps its due on its own table, and a stellar
/// directive's as a date.
/// </remarks>
public sealed class TerminativeService(EntityLifecycleResolver lifecycleResolver)
{
	/// <summary>
	/// Gets whether an item's due acts: it has a due, and the temporal-activation gate lets it act
	/// (<see cref="TemporalActivation.DueActs"/>).
	/// </summary>
	public bool Acts(IDueOwner origin)
	{
		ArgumentNullException.ThrowIfNull(origin);
		return origin.Due is not null && TemporalActivation.DueActs(origin, lifecycleResolver.StateOf(origin));
	}

	/// <summary>
	/// Gets the resolution an origin gives its terminative: pending while its due acts, resolved once the origin is finished
	/// or set aside.
	/// </summary>
	public TerminativeResolution ResolutionOf(IDueOwner origin)
		=> Acts(origin) ? TerminativeResolution.Pending : TerminativeResolution.Resolved;

	/// <summary>
	/// Builds the terminative an origin's due spawns, on its due and with the resolution its lifecycle gives.
	/// </summary>
	public Terminative Spawn(IDueOwner origin)
	{
		var terminative = Terminative.Of(origin);
		terminative.Resolution = ResolutionOf(origin);
		return terminative;
	}

	/// <summary>
	/// Brings a terminative in line with its origin: onto its due, with the resolution its lifecycle gives. Returns
	/// <see langword="false"/> when the origin no longer has a due, so the terminative has nothing left to mirror and the
	/// caller removes it; a terminative's user-set <see cref="Terminative.Ignored"/> flag is never touched.
	/// </summary>
	public bool Mirror(Terminative terminative, IDueOwner origin)
	{
		ArgumentNullException.ThrowIfNull(terminative);
		ArgumentNullException.ThrowIfNull(origin);
		if (origin.Due is not { } due)
		{
			return false;
		}

		terminative.Follow(due);
		var resolution = ResolutionOf(origin);
		if (terminative.Resolution != resolution)
		{
			terminative.Resolution = resolution;
		}

		return true;
	}

	/// <summary>
	/// Finds the items whose due acts and falls in <c>[<paramref name="fromInclusive"/>, <paramref name="toExclusive"/>)</c>
	/// on the civil clock their dues are written on, or before <paramref name="toExclusive"/> when no start is given. Read
	/// without their auto-includes, an objective with its directive and a stellar directive with its parent, and untracked
	/// unless <paramref name="tracking"/>.
	/// </summary>
	public async Task<List<IDueOwner>> FindActingAsync(
		PlainfraContext context,
		DateTime? fromInclusive,
		DateTime toExclusive,
		bool tracking,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		var found = new List<IDueOwner>();

		var activeObjectives = TemporalActivation.Statuses<ObjectiveStatus>();
		var objectives = Track(context.Objectives, tracking)
			.IgnoreAutoIncludes()
			.Include(objective => objective.Directive)
			.Where(objective => objective.Due != null && objective.Due.Moment < toExclusive && activeObjectives.Contains(objective.Status));
		if (fromInclusive is { } objectiveFrom)
		{
			objectives = objectives.Where(objective => objective.Due!.Moment >= objectiveFrom);
		}

		found.AddRange(await objectives.ToListAsync(cancellationToken));

		// A stellar directive's due is a date that falls due at its midnight.
		var activeDirectives = TemporalActivation.Statuses<DirectiveStatus>();
		var toDate = FirstDateFrom(toExclusive);
		var directives = Track(context.Directives.OfType<StellarDirective>(), tracking)
			.IgnoreAutoIncludes()
			.Include(directive => directive.ParentDirective)
			.Where(directive => directive.Due != null && directive.Due < toDate && activeDirectives.Contains(directive.Status));
		if (fromInclusive is { } directiveFrom)
		{
			var fromDate = FirstDateFrom(directiveFrom);
			directives = directives.Where(directive => directive.Due >= fromDate);
		}

		found.AddRange(await directives.ToListAsync(cancellationToken));

		// A checkpoint's due acts until it unlocks, set aside by the due or not.
		var checkpoints = Track(context.Checkpoints, tracking)
			.IgnoreAutoIncludes()
			.Where(checkpoint => checkpoint.Due != null && checkpoint.Due.Moment < toExclusive && !checkpoint.Unlocked);
		if (fromInclusive is { } checkpointFrom)
		{
			checkpoints = checkpoints.Where(checkpoint => checkpoint.Due!.Moment >= checkpointFrom);
		}

		found.AddRange(await checkpoints.ToListAsync(cancellationToken));
		return found.Where(Acts).ToList();
	}

	/// <summary>
	/// Finds an item under the due contract by its id, whichever kind it is, seeing an item added in the current unit of work
	/// before the store. Tracked, without its auto-includes.
	/// </summary>
	public async Task<IDueOwner?> FindOriginAsync(PlainfraContext context, string id, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		var added = context.ChangeTracker.Entries()
			.FirstOrDefault(entry => entry.State == EntityState.Added
				&& entry.Entity is IDueOwner owner
				&& string.Equals(owner.Id, id, StringComparison.OrdinalIgnoreCase))?.Entity as IDueOwner;
		if (added is not null)
		{
			return added;
		}

		return (IDueOwner?)await context.Objectives.IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			?? (IDueOwner?)await context.Directives.OfType<StellarDirective>().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			?? await context.Checkpoints.IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
	}

	/// <summary>
	/// Finds the terminatives of the given origins, seeing one added in the current unit of work before the store. Tracked.
	/// </summary>
	public async Task<List<Terminative>> FindOfAsync(PlainfraContext context, IReadOnlyCollection<IDueOwner> origins, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(origins);
		if (origins.Count == 0)
		{
			return [];
		}

		var found = context.ChangeTracker.Entries<Terminative>()
			.Where(entry => entry.State == EntityState.Added && origins.Any(entry.Entity.BelongsTo))
			.Select(entry => entry.Entity)
			.ToList();
		var objectiveIds = origins.OfType<Objective>().Select(item => item.Id).ToList();
		var directiveIds = origins.OfType<StellarDirective>().Select(item => item.Id).ToList();
		var checkpointIds = origins.OfType<Checkpoint>().Select(item => item.Id).ToList();
		found.AddRange(await context.Terminatives
			.IgnoreAutoIncludes()
			.Where(item => (item.ObjectiveId != null && objectiveIds.Contains(item.ObjectiveId))
				|| (item.DirectiveId != null && directiveIds.Contains(item.DirectiveId))
				|| (item.CheckpointId != null && checkpointIds.Contains(item.CheckpointId)))
			.ToListAsync(cancellationToken));
		return found.Distinct().ToList();
	}

	/// <summary>
	/// Finds the origin of each terminative, seeing an origin added or changed in the current unit of work. Tracked, without
	/// auto-includes. A terminative whose origin is gone is absent from the result.
	/// </summary>
	public async Task<Dictionary<Terminative, IDueOwner>> FindOriginsAsync(PlainfraContext context, IReadOnlyCollection<Terminative> terminatives, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(terminatives);
		var origins = new List<IDueOwner>();
		origins.AddRange(context.ChangeTracker.Entries()
			.Where(entry => entry.State == EntityState.Added && entry.Entity is IDueOwner)
			.Select(entry => (IDueOwner)entry.Entity));

		var objectiveIds = terminatives.Where(item => item.ObjectiveId != null).Select(item => item.ObjectiveId!).ToList();
		var directiveIds = terminatives.Where(item => item.DirectiveId != null).Select(item => item.DirectiveId!).ToList();
		var checkpointIds = terminatives.Where(item => item.CheckpointId != null).Select(item => item.CheckpointId!).ToList();
		// A tracking query yields the tracked instance where there is one, so a change pending in this unit of work is read.
		if (objectiveIds.Count > 0)
		{
			origins.AddRange(await context.Objectives.IgnoreAutoIncludes().Where(item => objectiveIds.Contains(item.Id)).ToListAsync(cancellationToken));
		}

		if (directiveIds.Count > 0)
		{
			origins.AddRange(await context.Directives.OfType<StellarDirective>().IgnoreAutoIncludes().Where(item => directiveIds.Contains(item.Id)).ToListAsync(cancellationToken));
		}

		if (checkpointIds.Count > 0)
		{
			origins.AddRange(await context.Checkpoints.IgnoreAutoIncludes().Where(item => checkpointIds.Contains(item.Id)).ToListAsync(cancellationToken));
		}

		var result = new Dictionary<Terminative, IDueOwner>();
		foreach (var terminative in terminatives)
		{
			if (origins.FirstOrDefault(terminative.BelongsTo) is { } origin)
			{
				result[terminative] = origin;
			}
		}

		return result;
	}

	private static IQueryable<T> Track<T>(IQueryable<T> query, bool tracking)
		where T : class
		=> tracking ? query : query.AsNoTracking();

	/// <summary>
	/// The first date whose midnight is at or after <paramref name="moment"/>.
	/// </summary>
	private static DateOnly FirstDateFrom(DateTime moment)
	{
		var date = DateOnly.FromDateTime(moment);
		return moment.TimeOfDay == TimeSpan.Zero ? date : date.AddDays(1);
	}
}
