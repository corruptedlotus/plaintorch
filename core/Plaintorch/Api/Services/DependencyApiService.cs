using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the dependency-facing PLAINTORCH application API (PEP101). Structural rules are enforced by
/// <see cref="DependencyRules"/>; endpoint existence and kind are validated here (they require resolving loose
/// references). Satisfaction and checkpoint unlock are computed by <see cref="DependencyReconciler"/> on save.
/// </summary>
public sealed class DependencyApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchStateService stateService,
	VaultAuditLogService auditLogService) : IDependencyApi
{
	private const string CheckpointTollDescriptionPrefix = "PLAINTORCH checkpoint toll";

	/// <inheritdoc />
	public async Task<Dependency> CreateAsync(EndpointRef source, EndpointRef target, DependencyTrigger? trigger = null, DependencyConstraint? constraint = null, CancellationToken cancellationToken = default)
	{
		await ValidateEndpointAsync("source", source, cancellationToken);
		await ValidateEndpointAsync("target", target, cancellationToken);

		var existing = await context.Dependencies.AsNoTracking().ToListAsync(cancellationToken);
		DependencyRules.EnsureValid(source, target, trigger, constraint, existing);
		await EnsureLogicallyPossibleAsync(source, target, trigger, constraint, cancellationToken);

		var dependency = new Dependency
		{
			SourceKind = source.Kind,
			SourceId = source.Id,
			SourceRecurrenceDate = source.RecurrenceDate,
			SourceRecurrenceTime = source.RecurrenceTime,
			TargetKind = target.Kind,
			TargetId = target.Id,
			TargetRecurrenceDate = target.RecurrenceDate,
			TargetRecurrenceTime = target.RecurrenceTime,
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
		return dependency;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Dependency>> ListAsync(string? entityId = null, CancellationToken cancellationToken = default)
	{
		var query = context.Dependencies.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(entityId))
		{
			query = query.Where(dependency => dependency.SourceId == entityId || dependency.TargetId == entityId);
		}

		return await query.OrderBy(dependency => dependency.Id).ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<EndpointHit>> SearchEndpointsAsync(SearchRequest search, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);

		// The same title/id substring match the objective and directive finders use, run over each endpoint
		// kind and unioned. An empty query returns a bounded slice rather than everything, so the picker opens
		// usefully before anything is typed. Only stellar directives take part — the OfType filter is what
		// excludes lunar directives — and decrees and Polaris-level records are simply never queried.
		// Checkpoints take part too, whichever sprint (or none) they belong to: a global context may pin one.
		var query = search.Query?.Trim();
		var hasQuery = !string.IsNullOrWhiteSpace(query);
		var perKind = search.Take is > 0 ? search.Take.Value : hasQuery ? 50 : 20;

		var directives = context.Directives
			.OfType<StellarDirective>()
			.AsNoTracking()
			.Where(directive => !hasQuery || directive.Id.Contains(query!) || directive.Title.Contains(query!))
			.OrderBy(directive => directive.Title)
			.Take(perKind)
			.Select(directive => new EndpointHit(DependencyEndpointKind.Directive, directive.Id, directive.Title));

		var objectives = context.Objectives
			.AsNoTracking()
			.Where(objective => !hasQuery || objective.Id.Contains(query!) || objective.Title.Contains(query!))
			.OrderBy(objective => objective.Title)
			.Take(perKind)
			.Select(objective => new EndpointHit(DependencyEndpointKind.Objective, objective.Id, objective.Title));

		var fates = context.Fates
			.AsNoTracking()
			.Where(fate => !hasQuery || fate.Id.Contains(query!) || fate.Title.Contains(query!))
			.OrderBy(fate => fate.Title)
			.Take(perKind)
			.Select(fate => new EndpointHit(DependencyEndpointKind.Fate, fate.Id, fate.Title));

		var checkpoints = context.Checkpoints
			.AsNoTracking()
			.Where(checkpoint => !hasQuery || checkpoint.Id.Contains(query!) || checkpoint.Title.Contains(query!))
			.OrderBy(checkpoint => checkpoint.Title)
			.Take(perKind)
			.Select(checkpoint => new EndpointHit(DependencyEndpointKind.Checkpoint, checkpoint.Id, checkpoint.Title));

		var hits = new List<EndpointHit>();
		hits.AddRange(await directives.ToListAsync(cancellationToken));
		hits.AddRange(await objectives.ToListAsync(cancellationToken));
		hits.AddRange(await fates.ToListAsync(cancellationToken));
		hits.AddRange(await checkpoints.ToListAsync(cancellationToken));
		return hits;
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

		var edges = await context.Dependencies
			.Where(dependency =>
				(dependency.SourceKind == DependencyEndpointKind.Checkpoint && dependency.SourceId == checkpointId)
				|| (dependency.TargetKind == DependencyEndpointKind.Checkpoint && dependency.TargetId == checkpointId))
			.ToListAsync(cancellationToken);
		context.Dependencies.RemoveRange(edges);
		context.Checkpoints.Remove(checkpoint);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"checkpoint.delete",
			subjectType: nameof(Checkpoint),
			subjectId: checkpointId,
			subjectTitle: checkpoint.Title,
			details: new { removedEdges = edges.Count },
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

	/// <summary>
	/// Whether the target's side (constraint, or a checkpoint's empty side) is on the shared begin/finish axis.
	/// </summary>
	private static object SideOf(DependencyEndpointKind kind, DependencyConstraint? constraint)
	{
		return kind == DependencyEndpointKind.Checkpoint
			? DBNull.Value
			: constraint == DependencyConstraint.ToFinish ? 1 : 0;
	}

	/// <inheritdoc cref="SideOf(DependencyEndpointKind, DependencyConstraint?)"/>
	private static object SideOf(DependencyEndpointKind kind, DependencyTrigger? trigger)
	{
		return kind == DependencyEndpointKind.Checkpoint
			? DBNull.Value
			: trigger == DependencyTrigger.OnBegin ? 0 : 1;
	}

	/// <summary>
	/// Rejects a dependency the existing graph makes logically impossible (PEP102).
	/// </summary>
	/// <remarks>
	/// The new edge asserts <c>source.trigger</c> comes before <c>target.constraint</c>. Impossible if the
	/// graph already orders them the other way — if, walking forward in time from the target's constrained
	/// side, the source's triggering side is already reachable. The walk lives in a two-sided state space
	/// (each node a begin=0 and a finish=1) over three steps: a dependency carries its source's triggering
	/// side to its dependant's constrained side; a node's begin precedes its own finish; and the start is the
	/// target's constrained side. A checkpoint has no side — its trigger and constraint are null — so it is a
	/// dead end here and drops out of the begin→finish step on its own, which is the intended handling.
	///
	/// This is temporal-cycle detection, and it is the whole of the cycle rule: it is strictly finer than the
	/// node-level acyclicity it replaced, since it permits the begin/finish cycles that are actually orderable.
	/// The trigger and constraint are stored as text and on different vocabularies, so both the stored edges
	/// and the prospective one are normalised to the shared begin/finish axis, resolving an omitted trigger to
	/// finish and an omitted constraint to begin, exactly as the engine reads them.
	/// </remarks>
	private async Task EnsureLogicallyPossibleAsync(EndpointRef source, EndpointRef target, DependencyTrigger? trigger, DependencyConstraint? constraint, CancellationToken cancellationToken)
	{
		// A recursive CTE run directly on the connection rather than through the query pipeline: the pipeline
		// wraps a raw statement as a subquery, and a top-level WITH cannot be wrapped that way.
		const string sql = @"
WITH RECURSIVE
edges(src, dst, sside, dside) AS (
    SELECT
        SourceKind || '|' || SourceId || '|' || COALESCE(SourceRecurrenceDate, '') || '|' || COALESCE(SourceRecurrenceTime, ''),
        TargetKind || '|' || TargetId || '|' || COALESCE(TargetRecurrenceDate, '') || '|' || COALESCE(TargetRecurrenceTime, ''),
        CASE WHEN SourceKind = 'Checkpoint' THEN NULL WHEN ""Trigger"" = 'OnBegin' THEN 0 ELSE 1 END,
        CASE WHEN TargetKind = 'Checkpoint' THEN NULL WHEN ""Constraint"" = 'ToFinish' THEN 1 ELSE 0 END
    FROM Dependencies
),
origin(node, side) AS (
    SELECT $sourceKind || '|' || $sourceId || '|' || COALESCE($sourceDate, '') || '|' || COALESCE($sourceTime, ''), $sourceSide
),
seed(node, side) AS (
    SELECT $targetKind || '|' || $targetId || '|' || COALESCE($targetDate, '') || '|' || COALESCE($targetTime, ''), $targetSide
),
path(node, side) AS (
    SELECT node, side FROM seed
    UNION
    SELECT e.dst, e.dside
        FROM path p JOIN edges e ON e.src = p.node AND e.sside = p.side
        WHERE p.node != (SELECT node FROM origin) OR p.side != (SELECT side FROM origin)
    UNION
    SELECT x.node, 1 FROM path x WHERE x.side = 0
)
SELECT NOT EXISTS (
    SELECT 1 FROM path JOIN origin ON path.node = origin.node AND path.side = origin.side
)";

		var connection = context.Database.GetDbConnection();
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
		AddParameter(command, "$sourceKind", source.Kind.ToString());
		AddParameter(command, "$sourceId", source.Id);
		AddParameter(command, "$sourceDate", (object?)source.RecurrenceDate ?? DBNull.Value);
		AddParameter(command, "$sourceTime", (object?)source.RecurrenceTime ?? DBNull.Value);
		AddParameter(command, "$targetKind", target.Kind.ToString());
		AddParameter(command, "$targetId", target.Id);
		AddParameter(command, "$targetDate", (object?)target.RecurrenceDate ?? DBNull.Value);
		AddParameter(command, "$targetTime", (object?)target.RecurrenceTime ?? DBNull.Value);
		AddParameter(command, "$sourceSide", SideOf(source.Kind, trigger));
		AddParameter(command, "$targetSide", SideOf(target.Kind, constraint));

		var opened = false;
		if (connection.State != ConnectionState.Open)
		{
			await connection.OpenAsync(cancellationToken);
			opened = true;
		}

		try
		{
			var possible = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
			if (possible == 0)
			{
				throw new InvalidOperationException(
					"This dependency is logically impossible: it would contradict the begin/finish ordering the existing dependencies already impose.");
			}
		}
		finally
		{
			if (opened)
			{
				await connection.CloseAsync();
			}
		}
	}

	private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
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
				if (endpoint.RecurrenceDate is null)
				{
					throw new InvalidOperationException($"The {side} eventive endpoint requires an occurrence date (RECURRENCE-ID).");
				}

				var ownerExists = await context.Fates.AnyAsync(item => item.Id == endpoint.Id, cancellationToken)
					|| await context.Objectives.AnyAsync(item => item.Id == endpoint.Id, cancellationToken);
				if (!ownerExists)
				{
					throw new InvalidOperationException($"The {side} eventive owner '{endpoint.Id}' was not found (expected a fate or objective).");
				}

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
		return endpoint.RecurrenceDate is DateOnly date
			? $"{endpoint.Kind}:{endpoint.Id}@{date:yyyy-MM-dd}"
			: $"{endpoint.Kind}:{endpoint.Id}";
	}
}
