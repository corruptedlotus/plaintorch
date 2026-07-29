namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Declares that a status enum member represents a lifecycle <see cref="LifecyclePhase"/> (PEP101). Applied to
/// the members of a status enum so the dependency system can resolve begin/finish without hardcoding any
/// per-entity status knowledge. A member may carry more than one phase.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public sealed class LifecyclePhaseAttribute(LifecyclePhase phase) : Attribute
{
	/// <summary>
	/// Gets the lifecycle phase the decorated status member represents.
	/// </summary>
	public LifecyclePhase Phase { get; } = phase;
}
