using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// Recomputes dependency satisfaction and checkpoint unlock state on every save (PEP101). Part of the
/// <see cref="StateEngine"/>'s reactions on the final state, so it sees the statuses the save leaves behind, compensations
/// included: it reads current tracked/stored state and flips persisted effects, propagating checkpoint unlocks to a fixpoint
/// so a chain of checkpoints resolves in one pass.
/// </summary>
public sealed class DependencyReconciler(EntityLifecycleResolver lifecycleResolver)
{
	private const int MaxIterations = 256;

	/// <summary>
	/// Whether a checkpoint's toll stands out of its way at <paramref name="now"/>: it has none, it is paid, or its due has
	/// not arrived (PEP111) — while there is still time, a checkpoint can unlock without paying.
	/// </summary>
	internal static bool TollMet(Checkpoint checkpoint, DateTime now)
		=> checkpoint.CelestronToll is null || checkpoint.TollPaid || (checkpoint.Due is { } due && now < due.Moment);

	/// <summary>
	/// Whether a checkpoint's external condition stands out of its way: it has none, or it holds.
	/// </summary>
	internal static bool ConditionMet(Checkpoint checkpoint) => checkpoint.ExternalCondition != false;

	/// <summary>
	/// Whether a checkpoint has something to wait for (PEP103 patch 5): a dependency into it, an external condition, or a toll
	/// that is owed (its due has passed, or it has none) or has been paid. A toll still waived while there is time holds
	/// nothing back.
	/// </summary>
	internal static bool Holds(Checkpoint checkpoint, int incoming, DateTime now)
		=> incoming > 0
			|| checkpoint.ExternalCondition is not null
			|| (checkpoint.CelestronToll is not null && (checkpoint.TollPaid || checkpoint.Due is not { } due || now >= due.Moment));

	/// <summary>
	/// Whether a checkpoint is unlocked — finished — once the save is through (PEP103 patch 5).
	/// <list type="bullet">
	/// <item><description>A dependency into it that does not hold, or an external condition that does not, locks it, unlocked
	/// or not: something new blocks it, so it takes back its finish.</description></item>
	/// <item><description>Once unlocked it stays so otherwise: what is done is done, so its due no longer brings its toll
	/// due, and losing what it waited for leaves it finished.</description></item>
	/// <item><description>A locked one unlocks only once it has had something to wait for (<see cref="Holds"/>) and all of it
	/// is met, its toll included. One with nothing to wait for — a sprint's milestone before anything leads into it — stays
	/// locked.</description></item>
	/// </list>
	/// </summary>
	/// <param name="checkpoint">The checkpoint, as the save leaves it; its <see cref="Checkpoint.Unlocked"/> is the state it
	/// stands in before this reading.</param>
	/// <param name="incoming">How many dependencies lead into it.</param>
	/// <param name="incomingMet">Whether every dependency into it holds.</param>
	/// <param name="now">The moment its due is read against.</param>
	internal static bool Unlocks(Checkpoint checkpoint, int incoming, bool incomingMet, DateTime now)
	{
		if (!incomingMet || !ConditionMet(checkpoint))
		{
			return false;
		}

		return checkpoint.Unlocked || (Holds(checkpoint, incoming, now) && TollMet(checkpoint, now));
	}

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

		var storedCheckpoints = await context.Checkpoints.ToListAsync(cancellationToken);
		var checkpoints = storedCheckpoints
			.Where(checkpoint => context.Entry(checkpoint).State != EntityState.Deleted)
			.Concat(context.ChangeTracker.Entries<Checkpoint>()
				.Where(entry => entry.State == EntityState.Added)
				.Select(entry => entry.Entity))
			.DistinctBy(checkpoint => checkpoint.Id, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(checkpoint => checkpoint.Id, StringComparer.OrdinalIgnoreCase);

		// A checkpoint is reconciled even with no edge in the vault: with nothing incoming, whether it unlocks — its
		// finish — rests on its toll and external condition alone, not on whether some unrelated edge exists.
		if (dependencies.Count == 0 && checkpoints.Count == 0)
		{
			return;
		}

		// Non-checkpoint sources have a fixed satisfaction for this save (their state does not change within the pass),
		// resolved together — one query per endpoint kind — rather than one at a time. A source with no entity behind
		// it (an occurrence still projected, an id naming nothing) satisfies nothing; a deferred one neither.
		var sourced = dependencies.Where(dependency => dependency.SourceKind != DependencyEndpointKind.Checkpoint).ToList();
		var lifecycles = await DependencyEndpoints.ResolveLifecyclesAsync(context, lifecycleResolver, sourced.Select(dependency => dependency.Source), cancellationToken);
		foreach (var dependency in sourced)
		{
			var satisfied = lifecycles.TryGetValue(dependency.Source, out var lifecycle)
				&& ((dependency.Trigger ?? DependencyTrigger.OnFinish) == DependencyTrigger.OnFinish ? lifecycle.Finished : lifecycle.Started);
			SetSatisfied(context, dependency, satisfied);
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
				changed |= SetUnlocked(context, checkpoint, Unlocks(checkpoint, incoming.Count, dependenciesMet, now));
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
