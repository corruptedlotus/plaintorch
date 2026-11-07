using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// Whether a conflict can be resolved by moving other items, and so whether the enforcement level governs it (PEP103).
/// </summary>
public enum StateConflictKind
{
	/// <summary>
	/// A lifecycle rule the requested change would break, which moving other items can resolve: an unmet dependency, an
	/// open child under a finishing parent. The enforcement level decides what happens.
	/// </summary>
	Lifecycle,

	/// <summary>
	/// A violation no state change resolves: a reference to nothing, a structure the model cannot hold, a value out of
	/// range. Refused at every enforcement level.
	/// </summary>
	Systemic,
}

/// <summary>
/// Names the item a conflict or a compensation is about (PEP103).
/// </summary>
/// <param name="Kind">The item's kind, as its type name (Objective, StellarDirective, Checkpoint…).</param>
/// <param name="Id">The item's id.</param>
/// <param name="Title">The item's title, when it has one, for the prompt a client shows.</param>
public sealed record StateSubject(string Kind, string Id, string? Title = null);

/// <summary>
/// One change the core would make to another item so a requested change can go ahead (PEP103).
/// </summary>
/// <param name="Subject">The item moved.</param>
/// <param name="From">The item's current state.</param>
/// <param name="To">The state the item is moved to.</param>
/// <param name="Status">
/// The name of the status that stands for <paramref name="To"/> on the item's kind — the lowest-valued one declaring it
/// (<see cref="EntityLifecycleResolver.DefaultStatus"/>) — or <see langword="null"/> when the kind reaches the state some other
/// way, as a checkpoint finishes by unlocking.
/// </param>
/// <param name="Reason">Why the move is needed, in words a prompt can show.</param>
public sealed record StateCompensation(StateSubject Subject, LifecycleState From, LifecycleState To, string? Status, string Reason);

/// <summary>
/// One rule a requested change would break, with the compensations that would resolve it (PEP103). The engine finds
/// conflicts; <see cref="StateDecisionService"/> decides what they mean under the enforcement level.
/// </summary>
/// <param name="Kind">Whether moving other items can resolve the conflict.</param>
/// <param name="Rule">A stable name for the broken rule (<c>dependency.start-gate</c>, <c>directive.open-children</c>…).</param>
/// <param name="Subject">The item the requested change is about.</param>
/// <param name="Message">What is wrong, in words a prompt can show.</param>
/// <param name="Compensations">
/// The moves that would resolve the conflict, all of them needed. Empty when it cannot be compensated, which a systemic
/// conflict never can.
/// </param>
public sealed record StateConflict(
	StateConflictKind Kind,
	string Rule,
	StateSubject Subject,
	string Message,
	IReadOnlyList<StateCompensation> Compensations)
{
	/// <summary>
	/// Gets whether the conflict can be resolved by its compensations: a lifecycle conflict that offers at least one.
	/// </summary>
	public bool IsCompensable => Kind == StateConflictKind.Lifecycle && Compensations.Count > 0;
}
