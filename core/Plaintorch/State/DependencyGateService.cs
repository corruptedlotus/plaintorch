using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Reads whether a target (blocked/dependant) is currently gated by unmet dependencies (PEP101), for what the API serves —
/// the lock view and each edge's <see cref="Dependency.GatesNextTransition"/> — and for whether an occurrence materialises.
/// Stored edges are read through their persisted <see cref="Dependency.Satisfied"/> flag, maintained by
/// <c>DependencyReconciler</c>, so they reflect the last reconciled state; intrinsic edges are derived on the spot (PEP102
/// patch 9). The emitted lock is separate from any status field. Holding a transition back is not this service's: the
/// state engine judges every save against the gates (<c>StateJudge</c>, PEP103).
/// </summary>
public sealed class DependencyGateService(PlainfraContext context, EntityLifecycleResolver lifecycleResolver, DependencyGraphService graphService)
{
	/// <summary>
	/// Determines whether the target has any unsatisfied dependency, stored or intrinsic, gating the given constraint phase.
	/// </summary>
	public async Task<bool> IsLockedAsync(EndpointRef target, DependencyConstraint phase, CancellationToken cancellationToken)
	{
		return await IsExplicitlyLockedAsync(target, phase, cancellationToken)
			|| await IsIntrinsicallyLockedAsync(target, phase, cancellationToken);
	}

	/// <summary>
	/// Determines whether the target has any unsatisfied stored dependency gating the given constraint phase.
	/// </summary>
	private async Task<bool> IsExplicitlyLockedAsync(EndpointRef target, DependencyConstraint phase, CancellationToken cancellationToken)
	{
		var query = context.Dependencies
			.AsNoTracking()
			.Where(dependency => dependency.TargetKind == target.Kind
				&& dependency.TargetId == target.Id
				&& !dependency.Satisfied);

		// The default constraint (null) is ToBegin, so a null-constraint dependency gates begin transitions.
		query = phase == DependencyConstraint.ToBegin
			? query.Where(dependency => dependency.Constraint == DependencyConstraint.ToBegin || dependency.Constraint == null)
			: query.Where(dependency => dependency.Constraint == phase);

		if (target.Kind == DependencyEndpointKind.Eventive && target.RecurrenceId is { } slot)
		{
			query = query.Where(dependency => dependency.TargetRecurrenceId == slot);
		}

		return await query.AnyAsync(cancellationToken);
	}

	/// <summary>
	/// Determines whether an intrinsic edge gates the target's given phase (PEP102 patch 9). Intrinsic edges only ever
	/// gate a finish, so a begin is never held. The target's own state is not consulted: the transition being judged
	/// is the one that leaves it, so its incoming edges are read as live even while it stands deferred.
	/// </summary>
	private async Task<bool> IsIntrinsicallyLockedAsync(EndpointRef target, DependencyConstraint phase, CancellationToken cancellationToken)
	{
		if (phase != DependencyConstraint.ToFinish || target.Kind != DependencyEndpointKind.Directive)
		{
			return false;
		}

		var intrinsic = await graphService.DeriveIntrinsicAsync(cancellationToken);
		return intrinsic.Any(dependency => dependency.Target == target && !dependency.Satisfied);
	}

	/// <summary>
	/// Stamps each dependency's <see cref="Dependency.GatesNextTransition"/>: whether, still unsatisfied, it gates the
	/// next lifecycle transition its target has to make. This is what separates what holds an entity back right now
	/// from what will only matter later (a finish gate on an entity that has not begun) or no longer matters (a begin
	/// gate on one that already has). Each satisfied one gets <see cref="Dependency.ReliedOn"/>: whether its target has
	/// passed the state it gates, so its source cannot move back freely (PEP103 patch 4). The distinct targets are resolved
	/// together, one query per kind.
	/// </summary>
	public async Task StampNextTransitionAsync(IEnumerable<Dependency> dependencies, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dependencies);
		var pending = dependencies.ToList();
		var targets = pending
			.Where(dependency => dependency.TargetKind is not (DependencyEndpointKind.Checkpoint or DependencyEndpointKind.Eventive)
				|| (!dependency.Satisfied && dependency.TargetKind != DependencyEndpointKind.Checkpoint))
			.Select(dependency => dependency.Target)
			.Distinct()
			.ToList();
		var lifecycles = await DependencyEndpoints.ResolveLifecyclesAsync(context, lifecycleResolver, targets, cancellationToken);

		foreach (var dependency in pending)
		{
			if (dependency.Satisfied)
			{
				dependency.GatesNextTransition = false;
				// The default constraint (null) is ToBegin.
				var gated = dependency.Constraint is DependencyConstraint.ToFinish ? LifecycleState.Finished : LifecycleState.Started;
				dependency.ReliedOn = dependency.TargetKind is not (DependencyEndpointKind.Checkpoint or DependencyEndpointKind.Eventive)
					&& lifecycles.TryGetValue(dependency.Target, out var target)
					&& target.State >= gated;
				continue;
			}

			dependency.ReliedOn = false;

			dependency.GatesNextTransition = ResolveNextTransition(dependency.Target, lifecycles) switch
			{
				NextTransition.Unlock => true,
				// The default constraint (null) is ToBegin, as in the explicit lock.
				NextTransition.Begin => dependency.Constraint is null or DependencyConstraint.ToBegin,
				NextTransition.Finish => dependency.Constraint is DependencyConstraint.ToFinish,
				_ => false,
			};
		}
	}

	/// <summary>
	/// The transition a target has to make next, read off its lifecycle (<see cref="EntityLifecycleResolver.NextGatedState"/>).
	/// A checkpoint never starts, so its next transition is its unlock (its finish), which every incoming dependency gates.
	/// An endpoint with no entity behind it — an occurrence still projected from its schedule — has not started, and neither
	/// has a deferred one.
	/// </summary>
	private static NextTransition ResolveNextTransition(EndpointRef target, IReadOnlyDictionary<EndpointRef, EndpointLifecycle> lifecycles)
	{
		if (target.Kind == DependencyEndpointKind.Checkpoint)
		{
			return NextTransition.Unlock;
		}

		if (!lifecycles.TryGetValue(target, out var lifecycle))
		{
			return NextTransition.Begin;
		}

		return lifecycle.NextGated switch
		{
			LifecycleState.Started => NextTransition.Begin,
			LifecycleState.Finished => NextTransition.Finish,
			_ => NextTransition.None,
		};
	}

	/// <summary>The lifecycle transition a dependency target has to make next.</summary>
	private enum NextTransition
	{
		Begin,
		Finish,
		Unlock,
		None,
	}

	/// <summary>
	/// Determines whether materializing a fate occurrence is blocked (PEP101): either the whole fate is frozen
	/// (a locked whole-fate begin constraint pauses orbit generation and blocks all single events) or the
	/// specific occurrence slot is itself locked.
	/// </summary>
	public async Task<bool> IsFateMaterializationBlockedAsync(string fateId, DateTime? recurrenceId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		if (await IsLockedAsync(new EndpointRef(DependencyEndpointKind.Fate, fateId), DependencyConstraint.ToBegin, cancellationToken))
		{
			return true;
		}

		if (recurrenceId is null)
		{
			return false;
		}

		return await IsLockedAsync(new EndpointRef(DependencyEndpointKind.Eventive, fateId, recurrenceId), DependencyConstraint.ToBegin, cancellationToken);
	}
}
