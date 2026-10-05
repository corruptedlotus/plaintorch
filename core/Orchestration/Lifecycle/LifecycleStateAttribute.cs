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
}
