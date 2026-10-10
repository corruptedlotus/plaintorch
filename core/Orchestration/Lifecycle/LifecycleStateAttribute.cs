namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Declares the <see cref="LifecycleState"/> a status enum member stands for (PEP103). Every member of a lifecycle status
/// enum carries exactly one, so the lifecycle of a kind is read off its declarations rather than written into the rules
/// that need it.
/// </summary>
/// <remarks>
/// Several members may share a state: an objective is finished when it is Done or Archived. When the core has to pick a
/// status for a state on its own, it takes the member with the lowest value among those that declare it
/// (<see cref="LifecycleDeclarations.DefaultStatus(Type, LifecycleState)"/>).
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class LifecycleStateAttribute(LifecycleState state) : Attribute
{
	/// <summary>
	/// Gets the lifecycle state the decorated status member stands for.
	/// </summary>
	public LifecycleState State { get; } = state;

	/// <summary>
	/// Gets or sets whether the status closes its item (PEP103 patch 5): a finished status that stands only while everything
	/// the item depends on holds — an objective's Archived, a directive's Over. A dependency added to a finished item, or one
	/// that stops holding, only keeps it from closing; an item already closed steps back to its kind's default finished
	/// status. Only a <see cref="LifecycleState.Finished"/> member may close.
	/// </summary>
	public bool Closed { get; init; }
}
