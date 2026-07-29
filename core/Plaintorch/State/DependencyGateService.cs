using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Answers whether a target (blocked/dependant) is currently gated by unmet dependencies (PEP101). Reads the
/// persisted <see cref="Dependency.Satisfied"/> flag maintained by <see cref="DependencyReconciler"/>, so it
/// reflects the last reconciled state. The emitted lock is separate from any status field.
/// </summary>
public sealed class DependencyGateService(PlainfraContext context, EntityLifecycleResolver lifecycleResolver)
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

		if (await IsLockedAsync(target, gatedPhase.Value, cancellationToken))
		{
			throw new InvalidOperationException(
				$"{Describe(target)} is blocked by unmet dependencies and cannot {(finish ? "finish" : "begin")} until they are satisfied.");
		}
	}

	/// <summary>
	/// Determines whether the target has any unsatisfied dependency gating the given constraint phase.
	/// </summary>
	public async Task<bool> IsLockedAsync(EndpointRef target, DependencyConstraint phase, CancellationToken cancellationToken)
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

		if (target.Kind == DependencyEndpointKind.Eventive && target.RecurrenceDate is DateOnly date)
		{
			query = target.RecurrenceTime is TimeOnly time
				? query.Where(dependency => dependency.TargetRecurrenceDate == date && dependency.TargetRecurrenceTime == time)
				: query.Where(dependency => dependency.TargetRecurrenceDate == date && dependency.TargetRecurrenceTime == null);
		}

		return await query.AnyAsync(cancellationToken);
	}

	/// <summary>
	/// Determines whether materializing a fate occurrence is blocked (PEP101): either the whole fate is frozen
	/// (a locked whole-fate begin constraint pauses orbit generation and blocks all single events) or the
	/// specific occurrence slot is itself locked.
	/// </summary>
	public async Task<bool> IsFateMaterializationBlockedAsync(string fateId, DateOnly? occurrenceDate, TimeOnly? occurrenceTime, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		if (await IsLockedAsync(new EndpointRef(DependencyEndpointKind.Fate, fateId), DependencyConstraint.ToBegin, cancellationToken))
		{
			return true;
		}

		if (occurrenceDate is null)
		{
			return false;
		}

		return await IsLockedAsync(new EndpointRef(DependencyEndpointKind.Eventive, fateId, occurrenceDate, occurrenceTime), DependencyConstraint.ToBegin, cancellationToken);
	}

	private static string Describe(EndpointRef target)
	{
		return target.Kind switch
		{
			DependencyEndpointKind.Eventive => $"Occurrence {target.RecurrenceDate:yyyy-MM-dd} of '{target.Id}'",
			_ => $"{target.Kind} '{target.Id}'",
		};
	}
}
