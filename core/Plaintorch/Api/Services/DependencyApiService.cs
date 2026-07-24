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
	public async Task<Checkpoint> CreateCheckpointAsync(string title, string? requestedId = null, int? celestronToll = null, bool? externalCondition = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		if (celestronToll is < 0)
		{
			throw new InvalidOperationException("A checkpoint's Celestron toll cannot be negative.");
		}

		var checkpoint = new Checkpoint
		{
			Id = puckCreationService.CreateIdFor<Checkpoint>(requestedId),
			Title = title,
			CelestronToll = celestronToll,
			ExternalCondition = externalCondition,
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
