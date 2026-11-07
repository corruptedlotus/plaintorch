using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the dependency-facing PLAINTORCH application API (PEP101). Structural rules are enforced by
/// <see cref="DependencyRules"/> and the temporal cycle rule by <see cref="DependencyOrdering"/>, both over the whole
/// graph — the stored edges and the intrinsic ones the structure implies (PEP102 patch 9), read through
/// <see cref="DependencyGraphService"/>; endpoint existence and kind are validated here (they require resolving loose
/// references). Whether an eventive endpoint's slot names a real occurrence is judged by the state engine on the save
/// (<see cref="Enforcement.StateJudge.OccurrenceRule"/>), never here. Satisfaction and checkpoint unlock are computed by <see cref="Enforcement.DependencyReconciler"/> on save; whether
/// an unsatisfied edge gates its target's next transition is stamped by <see cref="DependencyGateService"/> as it is served.
/// </summary>
public sealed class DependencyApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchStateService stateService,
	DependencyGateService gateService,
	DependencyGraphService graphService,
	VaultAuditLogService auditLogService) : IDependencyApi
{
	private const string CheckpointTollDescriptionPrefix = "PLAINTORCH checkpoint toll";

	// How many hits of each endpoint kind a search returns when the caller names no cap.
	private const int DefaultEndpointHitsPerKind = 50;

	/// <inheritdoc />
	public async Task<Dependency> CreateAsync(EndpointRef source, EndpointRef target, DependencyTrigger? trigger = null, DependencyConstraint? constraint = null, CancellationToken cancellationToken = default)
	{
		await ValidateEndpointAsync("source", source, cancellationToken);
		await ValidateEndpointAsync("target", target, cancellationToken);

		// The intrinsic edges take part in both rules: one that restates a child's finish gating its parent's finish
		// is a duplicate, and one that orders against the structure is impossible.
		var graph = await graphService.LoadGraphAsync(cancellationToken);
		DependencyRules.EnsureValid(source, target, trigger, constraint, graph.All);
		if (DependencyOrdering.FindContradiction(graph.All, source, trigger, target, constraint) is { } contradiction)
		{
			throw new InvalidOperationException(
				"This dependency is logically impossible: it would contradict the begin/finish ordering the existing dependencies already impose ("
				+ DependencyOrdering.Describe(contradiction) + ").");
		}

		var dependency = new Dependency
		{
			SourceKind = source.Kind,
			SourceId = source.Id,
			SourceRecurrenceId = source.RecurrenceId,
			TargetKind = target.Kind,
			TargetId = target.Id,
			TargetRecurrenceId = target.RecurrenceId,
			Trigger = trigger,
			Constraint = constraint,
		};

		context.Dependencies.Add(dependency);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"dependency.create",
			subjectType: nameof(Dependency),
			subjectId: dependency.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { source = Describe(source), target = Describe(target), trigger, constraint, dependency.Satisfied },
			cancellationToken: cancellationToken);
		await gateService.StampNextTransitionAsync([dependency], cancellationToken);
		return dependency;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Dependency>> ListAsync(string? entityId = null, CancellationToken cancellationToken = default)
	{
		var query = context.Dependencies.AsNoTracking();
		var intrinsic = await graphService.DeriveIntrinsicAsync(cancellationToken);
		if (!string.IsNullOrWhiteSpace(entityId))
		{
			query = query.Where(dependency => dependency.SourceId == entityId || dependency.TargetId == entityId);
			intrinsic = intrinsic
				.Where(dependency => string.Equals(dependency.SourceId, entityId, StringComparison.Ordinal) || string.Equals(dependency.TargetId, entityId, StringComparison.Ordinal))
				.ToList();
		}

		// Stored edges first, in their own order; the intrinsic ones after, as they carry no identity to order by.
		var dependencies = (await query.OrderBy(dependency => dependency.Id).ToListAsync(cancellationToken)).Concat(intrinsic).ToList();
		await gateService.StampNextTransitionAsync(dependencies, cancellationToken);
		return dependencies;
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<EndpointHit>> ListEndpointsAsync(CancellationToken cancellationToken = default)
		=> CollectEndpointsAsync(search: null, perKind: null, cancellationToken);

	/// <inheritdoc />
	public Task<IReadOnlyList<EndpointHit>> SearchEndpointsAsync(SearchRequest search, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentException.ThrowIfNullOrWhiteSpace(search.Query);

		return CollectEndpointsAsync(search.Query, search.Take is > 0 ? search.Take.Value : DefaultEndpointHitsPerKind, cancellationToken);
	}

	// Each endpoint kind is matched, title-ordered and capped on its own, then the kinds are unioned. Only stellar
	// directives take part — the OfType filter is what excludes lunar directives — and decrees and Polaris-level
	// records are simply never queried. Checkpoints take part too, whichever sprint (or none) they belong to: a
	// global context may pin one.
	private async Task<IReadOnlyList<EndpointHit>> CollectEndpointsAsync(string? search, int? perKind, CancellationToken cancellationToken)
	{
		var directives = Capped(context.Directives
				.OfType<StellarDirective>()
				.AsNoTracking()
				.WhereMatchesSearch(search, directive => directive.Id, directive => directive.Title)
				.OrderBy(directive => directive.Title), perKind)
			.Select(directive => new EndpointHit(DependencyEndpointKind.Directive, directive.Id, directive.Title));

		var objectives = Capped(context.Objectives
				.AsNoTracking()
				.WhereMatchesSearch(search, objective => objective.Id, objective => objective.Title)
				.OrderBy(objective => objective.Title), perKind)
			.Select(objective => new EndpointHit(DependencyEndpointKind.Objective, objective.Id, objective.Title));

		var fates = Capped(context.Fates
				.AsNoTracking()
				.WhereMatchesSearch(search, fate => fate.Id, fate => fate.Title)
				.OrderBy(fate => fate.Title), perKind)
			.Select(fate => new EndpointHit(DependencyEndpointKind.Fate, fate.Id, fate.Title));

		var checkpoints = Capped(context.Checkpoints
				.AsNoTracking()
				.WhereMatchesSearch(search, checkpoint => checkpoint.Id, checkpoint => checkpoint.Title)
				.OrderBy(checkpoint => checkpoint.Title), perKind)
			.Select(checkpoint => new EndpointHit(DependencyEndpointKind.Checkpoint, checkpoint.Id, checkpoint.Title));

		var hits = new List<EndpointHit>();
		hits.AddRange(await directives.ToListAsync(cancellationToken));
		hits.AddRange(await objectives.ToListAsync(cancellationToken));
		hits.AddRange(await fates.ToListAsync(cancellationToken));
		hits.AddRange(await checkpoints.ToListAsync(cancellationToken));
		return hits;

		static IQueryable<T> Capped<T>(IQueryable<T> query, int? take) => take is { } count ? query.Take(count) : query;
	}

	/// <inheritdoc />
	public async Task DeleteAsync(long dependencyId, CancellationToken cancellationToken = default)
	{
		var dependency = await context.Dependencies.FirstOrDefaultAsync(item => item.Id == dependencyId, cancellationToken)
			?? throw new InvalidOperationException($"Dependency '{dependencyId}' was not found.");
		context.Dependencies.Remove(dependency);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"dependency.delete",
			subjectType: nameof(Dependency),
			subjectId: dependencyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async Task<DependencyLockView> GetLockAsync(string entityId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		var unsatisfied = await context.Dependencies
			.AsNoTracking()
			.Where(dependency => dependency.TargetId == entityId && !dependency.Satisfied)
			.OrderBy(dependency => dependency.Id)
			.ToListAsync(cancellationToken);
		unsatisfied.AddRange((await graphService.DeriveIntrinsicAsync(cancellationToken))
			.Where(dependency => string.Equals(dependency.TargetId, entityId, StringComparison.Ordinal) && !dependency.Satisfied));
		await gateService.StampNextTransitionAsync(unsatisfied, cancellationToken);

		var blockedBegin = unsatisfied.Any(dependency => dependency.Constraint is null or DependencyConstraint.ToBegin);
		var blockedFinish = unsatisfied.Any(dependency => dependency.Constraint is DependencyConstraint.ToFinish);
		return new DependencyLockView(entityId, blockedBegin, blockedFinish, unsatisfied);
	}

	/// <inheritdoc />
	public async Task<Checkpoint> CreateCheckpointAsync(string title, string? requestedId = null, int? celestronToll = null, bool? externalCondition = null, string? onrushSprintId = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		if (celestronToll is < 0)
		{
			throw new InvalidOperationException("A checkpoint's Celestron toll cannot be negative.");
		}

		if (!string.IsNullOrWhiteSpace(onrushSprintId)
			&& !await context.OnrushSprints.AnyAsync(sprint => sprint.Id == onrushSprintId, cancellationToken))
		{
			throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
		}

		var checkpoint = new Checkpoint
		{
			Id = puckCreationService.CreateIdFor<Checkpoint>(requestedId),
			Title = title,
			CelestronToll = celestronToll,
			ExternalCondition = externalCondition,
			OnrushSprintId = string.IsNullOrWhiteSpace(onrushSprintId) ? null : onrushSprintId,
		};

		context.Checkpoints.Add(checkpoint);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync("api", "checkpoint.create", subject: checkpoint, cancellationToken: cancellationToken);
		return checkpoint;
	}

	/// <inheritdoc />
	public Task<Checkpoint?> GetCheckpointAsync(string checkpointId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
		return context.Checkpoints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == checkpointId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Checkpoint> UpdateCheckpointAsync(string checkpointId, CheckpointUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
		ArgumentNullException.ThrowIfNull(update);
		var checkpoint = await context.Checkpoints.FirstOrDefaultAsync(item => item.Id == checkpointId, cancellationToken)
			?? throw new InvalidOperationException($"Checkpoint '{checkpointId}' was not found.");

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			checkpoint.Title = update.Title;
		}

		if (update.CelestronToll.IsSet)
		{
			if (update.CelestronToll.Value is int toll)
			{
				if (toll < 0)
				{
					throw new InvalidOperationException("A checkpoint's Celestron toll cannot be negative.");
				}

				checkpoint.CelestronToll = toll;
			}
			else
			{
				// Removing the toll removes what there was to pay, so the paid flag no longer means anything.
				checkpoint.CelestronToll = null;
				checkpoint.TollPaid = false;
			}
		}

		if (update.ExternalCondition.IsSet)
		{
			checkpoint.ExternalCondition = update.ExternalCondition.Value;
		}

		if (update.Due.IsSet)
		{
			// A due suppresses the toll until it arrives; clearing it (null) makes any toll owed immediately.
			checkpoint.Due = update.Due.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync("api", "checkpoint.update", subject: checkpoint, cancellationToken: cancellationToken);
		return checkpoint;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Checkpoint>> ListCheckpointsAsync(CancellationToken cancellationToken = default)
	{
		return await context.Checkpoints.AsNoTracking().OrderBy(item => item.Title).ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task DeleteCheckpointAsync(string checkpointId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
		var checkpoint = await context.Checkpoints.FirstOrDefaultAsync(item => item.Id == checkpointId, cancellationToken)
			?? throw new InvalidOperationException($"Checkpoint '{checkpointId}' was not found.");

		// A milestone stands for its onrush's completion and is bound to it for the sprint's life (PEP102):
		// it goes only when the sprint does, never on its own.
		if (await context.OnrushSprints.AnyAsync(sprint => sprint.MilestoneCheckpointId == checkpointId, cancellationToken))
		{
			throw new InvalidOperationException("A sprint's milestone checkpoint cannot be deleted on its own.");
		}

		// Its edges go with it: the state engine releases the edges of a deleted endpoint, on every pathway.
		var removedEdges = await context.Dependencies
			.CountAsync(dependency =>
				(dependency.SourceKind == DependencyEndpointKind.Checkpoint && dependency.SourceId == checkpointId)
				|| (dependency.TargetKind == DependencyEndpointKind.Checkpoint && dependency.TargetId == checkpointId),
				cancellationToken);
		context.Checkpoints.Remove(checkpoint);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"checkpoint.delete",
			subjectType: nameof(Checkpoint),
			subjectId: checkpointId,
			subjectTitle: checkpoint.Title,
			details: new { removedEdges },
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Checkpoint> PayTollAsync(string checkpointId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
		var checkpoint = await context.Checkpoints.FirstOrDefaultAsync(item => item.Id == checkpointId, cancellationToken)
			?? throw new InvalidOperationException($"Checkpoint '{checkpointId}' was not found.");

		if (checkpoint.CelestronToll is not int toll)
		{
			throw new InvalidOperationException($"Checkpoint '{checkpointId}' has no Celestron toll to pay.");
		}

		if (checkpoint.TollPaid)
		{
			return checkpoint;
		}

		var banked = await stateService.GetCelestronBankedAsync(cancellationToken);
		if (banked < toll)
		{
			throw new InvalidOperationException($"Insufficient Celestron to pay the toll: {banked} banked, {toll} required.");
		}

		context.CelestronLedger.Add(new CelestronTransaction
		{
			Amount = -toll,
			SourcePuck = checkpoint.Id,
			Description = $"{CheckpointTollDescriptionPrefix} ({checkpoint.Id})",
		});
		checkpoint.TollPaid = true;
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"checkpoint.pay-toll",
			subject: checkpoint,
			details: new { toll, unlocked = checkpoint.Unlocked },
			cancellationToken: cancellationToken);
		return checkpoint;
	}

	/// <inheritdoc />
	public async Task<Checkpoint> SetExternalConditionAsync(string checkpointId, bool met, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
		var checkpoint = await context.Checkpoints.FirstOrDefaultAsync(item => item.Id == checkpointId, cancellationToken)
			?? throw new InvalidOperationException($"Checkpoint '{checkpointId}' was not found.");

		if (checkpoint.ExternalCondition is null)
		{
			throw new InvalidOperationException($"Checkpoint '{checkpointId}' has no external condition to set.");
		}

		checkpoint.ExternalCondition = met;
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"checkpoint.set-condition",
			subject: checkpoint,
			details: new { met, unlocked = checkpoint.Unlocked },
			cancellationToken: cancellationToken);
		return checkpoint;
	}

	private async Task ValidateEndpointAsync(string side, EndpointRef endpoint, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Id);
		switch (endpoint.Kind)
		{
			case DependencyEndpointKind.Directive:
				if (!await context.Directives.OfType<StellarDirective>().AnyAsync(item => item.Id == endpoint.Id, cancellationToken))
				{
					throw new InvalidOperationException($"The {side} stellar directive '{endpoint.Id}' was not found (lunar directives cannot participate in dependencies).");
				}

				break;
			case DependencyEndpointKind.Objective:
				await EnsureExistsAsync(side, "objective", await context.Objectives.AnyAsync(item => item.Id == endpoint.Id, cancellationToken), endpoint.Id);
				break;
			case DependencyEndpointKind.Fate:
				await EnsureExistsAsync(side, "fate", await context.Fates.AnyAsync(item => item.Id == endpoint.Id, cancellationToken), endpoint.Id);
				break;
			case DependencyEndpointKind.Checkpoint:
				await EnsureExistsAsync(side, "checkpoint", await context.Checkpoints.AnyAsync(item => item.Id == endpoint.Id, cancellationToken), endpoint.Id);
				break;
			case DependencyEndpointKind.Eventive:
				if (endpoint.RecurrenceId is null)
				{
					throw new InvalidOperationException($"The {side} eventive endpoint requires an occurrence date (RECURRENCE-ID).");
				}

				// Only a fate owns eventives: an item's due is a terminative, which no edge names (PEP103 patch 7).
				if (!await context.Fates.AnyAsync(item => item.Id == endpoint.Id, cancellationToken))
				{
					throw new InvalidOperationException($"The {side} eventive owner '{endpoint.Id}' was not found (expected a fate).");
				}

				// Whether the slot resolves to an occurrence is the state engine's to judge, on every pathway: SH-17 hardens
				// only an exact one, and the judge refuses the edge otherwise (dependency.occurrence, PEP103 I-035).
				break;
			default:
				throw new InvalidOperationException($"The {side} endpoint kind '{endpoint.Kind}' is not supported.");
		}
	}

	private static Task EnsureExistsAsync(string side, string kind, bool exists, string id)
	{
		if (!exists)
		{
			throw new InvalidOperationException($"The {side} {kind} '{id}' was not found.");
		}

		return Task.CompletedTask;
	}

	private static string Describe(EndpointRef endpoint)
	{
		return endpoint.RecurrenceId is { } slot
			? $"{endpoint.Kind}:{endpoint.Id}@{slot:yyyy-MM-dd}"
			: $"{endpoint.Kind}:{endpoint.Id}";
	}
}
