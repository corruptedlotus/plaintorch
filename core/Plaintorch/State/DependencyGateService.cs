using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Answers whether a target (blocked/dependant) is currently gated by unmet dependencies (PEP101). Stored edges are
/// read through their persisted <see cref="Dependency.Satisfied"/> flag, maintained by <see cref="DependencyReconciler"/>,
/// so they reflect the last reconciled state; intrinsic edges are derived on the spot (PEP102 patch 9). The emitted
/// lock is separate from any status field.
/// </summary>
public sealed class DependencyGateService(PlainfraContext context, EntityLifecycleResolver lifecycleResolver, DependencyGraphService graphService)
{
	/// <summary>
	/// Throws when a workflow shift to <paramref name="newStatus"/> would move <paramref name="target"/> through
	/// a transition still gated by unmet dependencies. A shift into a non-begin/non-finish status is never gated.
	/// </summary>
	public async Task EnsureCanTransitionAsync(EndpointRef target, Enum newStatus, CancellationToken cancellationToken)
	{
		var (begin, finish) = lifecycleResolver.ClassifyStatus(newStatus);
		DependencyConstraint? gatedPhase = finish
			? DependencyConstraint.ToFinish
			: begin ? DependencyConstraint.ToBegin : null;
		if (gatedPhase is null)
		{
			return;
		}

		if (await IsExplicitlyLockedAsync(target, gatedPhase.Value, cancellationToken))
		{
			throw new InvalidOperationException(
				$"{Describe(target)} is blocked by unmet dependencies and cannot {(finish ? "finish" : "begin")} until they are satisfied.");
		}

		if (await IsIntrinsicallyLockedAsync(target, gatedPhase.Value, cancellationToken))
		{
			throw new InvalidOperationException(
				$"{Describe(target)} still has open children and cannot finish until each of them has finished.");
		}
	}

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
	/// is the one that leaves it, so its incoming edges are read as live even while it stands failed.
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
	/// gate on one that already has). The distinct targets are resolved together, one query per kind.
	/// </summary>
	public async Task StampNextTransitionAsync(IEnumerable<Dependency> dependencies, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dependencies);
		var pending = dependencies.ToList();
		var targets = pending
			.Where(dependency => !dependency.Satisfied && dependency.TargetKind != DependencyEndpointKind.Checkpoint)
			.Select(dependency => dependency.Target)
			.Distinct()
			.ToList();
		var lifecycles = await DependencyEndpoints.ResolveLifecyclesAsync(context, lifecycleResolver, targets, cancellationToken);

		foreach (var dependency in pending)
		{
			if (dependency.Satisfied)
			{
				dependency.GatesNextTransition = false;
				continue;
			}

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
	/// The transition a target has to make next. A checkpoint is not a lifecycle kind: its next transition is its
	/// unlock, which every incoming dependency gates. An endpoint with no entity behind it — an occurrence still
	/// projected from its schedule — has not begun, and neither has a failed one.
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

		if (lifecycle.Finished)
		{
			return NextTransition.None;
		}

		return lifecycle.Begun ? NextTransition.Finish : NextTransition.Begin;
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

	private static string Describe(EndpointRef target)
	{
		return target.Kind switch
		{
			DependencyEndpointKind.Eventive => $"Occurrence {target.RecurrenceId:yyyy-MM-dd} of '{target.Id}'",
			_ => $"{target.Kind} '{target.Id}'",
		};
	}
}
