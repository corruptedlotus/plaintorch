using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Recomputes dependency satisfaction and checkpoint unlock state on every save (PEP101). Runs inside the
/// state-policy pass (and therefore the save interceptor), mirroring the reflective-collection reward rule:
/// it reads current tracked/stored state and flips persisted effects, propagating checkpoint unlocks to a
/// fixpoint so a chain of checkpoints resolves in one pass.
/// </summary>
public sealed class DependencyReconciler(EntityLifecycleResolver lifecycleResolver)
{
	private const int MaxIterations = 256;

	/// <summary>
	/// Reconciles all dependency edges and checkpoints in the current unit of work.
	/// </summary>
	public async Task ReconcileAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		var stored = await context.Dependencies.ToListAsync(cancellationToken);
		var dependencies = stored
			.Where(dependency => context.Entry(dependency).State != EntityState.Deleted)
			.Concat(context.ChangeTracker.Entries<Dependency>()
				.Where(entry => entry.State == EntityState.Added)
				.Select(entry => entry.Entity))
			.ToList();

		if (dependencies.Count == 0)
		{
			return;
		}

		var storedCheckpoints = await context.Checkpoints.ToListAsync(cancellationToken);
		var checkpoints = storedCheckpoints
			.Where(checkpoint => context.Entry(checkpoint).State != EntityState.Deleted)
			.Concat(context.ChangeTracker.Entries<Checkpoint>()
				.Where(entry => entry.State == EntityState.Added)
				.Select(entry => entry.Entity))
			.DistinctBy(checkpoint => checkpoint.Id, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(checkpoint => checkpoint.Id, StringComparer.OrdinalIgnoreCase);

		// Non-checkpoint sources have a fixed satisfaction for this save (their state does not change within the pass).
		foreach (var dependency in dependencies.Where(dependency => dependency.SourceKind != DependencyEndpointKind.Checkpoint))
		{
			SetSatisfied(context, dependency, await ResolveNonCheckpointSatisfiedAsync(context, dependency, cancellationToken));
		}

		var incomingByCheckpoint = dependencies
			.Where(dependency => dependency.TargetKind == DependencyEndpointKind.Checkpoint)
			.GroupBy(dependency => dependency.TargetId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
		var checkpointSourced = dependencies
			.Where(dependency => dependency.SourceKind == DependencyEndpointKind.Checkpoint)
			.ToList();

		var now = DateTime.Now;
		for (var iteration = 0; iteration < MaxIterations; iteration++)
		{
			var changed = false;

			foreach (var checkpoint in checkpoints.Values)
			{
				var incoming = incomingByCheckpoint.GetValueOrDefault(checkpoint.Id) ?? [];
				var dependenciesMet = incoming.All(dependency => dependency.Satisfied);
				// The toll is suppressed until the due arrives (PEP111): while there is still time, the checkpoint can
				// unlock without paying; once the due moment has passed, the toll is owed as usual.
				var beforeDue = checkpoint.Due is { } due && now < due.Moment;
				var tollMet = checkpoint.CelestronToll is null || checkpoint.TollPaid || beforeDue;
				var conditionMet = checkpoint.ExternalCondition != false;
				changed |= SetUnlocked(context, checkpoint, dependenciesMet && tollMet && conditionMet);
			}

			foreach (var dependency in checkpointSourced)
			{
				var satisfied = checkpoints.TryGetValue(dependency.SourceId, out var checkpoint) && checkpoint.Unlocked;
				changed |= SetSatisfied(context, dependency, satisfied);
			}

			if (!changed)
			{
				break;
			}
		}
	}

	private async Task<bool> ResolveNonCheckpointSatisfiedAsync(PlainfraContext context, Dependency dependency, CancellationToken cancellationToken)
	{
		var entity = await DependencyEndpoints.ResolveEntityAsync(context, dependency.Source, cancellationToken);
		if (entity is null)
		{
			return false;
		}

		return (dependency.Trigger ?? DependencyTrigger.OnFinish) == DependencyTrigger.OnFinish
			? lifecycleResolver.HasFinished(entity)
			: lifecycleResolver.HasBegun(entity);
	}

	private static bool SetSatisfied(PlainfraContext context, Dependency dependency, bool satisfied)
	{
		if (dependency.Satisfied == satisfied)
		{
			return false;
		}

		dependency.Satisfied = satisfied;
		MarkModified(context, dependency, nameof(Dependency.Satisfied));
		return true;
	}

	private static bool SetUnlocked(PlainfraContext context, Checkpoint checkpoint, bool unlocked)
	{
		if (checkpoint.Unlocked == unlocked)
		{
			return false;
		}

		checkpoint.Unlocked = unlocked;
		MarkModified(context, checkpoint, nameof(Checkpoint.Unlocked));
		return true;
	}

	private static void MarkModified(PlainfraContext context, object entity, string propertyName)
	{
		var entry = context.Entry(entity);
		if (entry.State != EntityState.Added)
		{
			entry.Property(propertyName).IsModified = true;
		}
	}
}
