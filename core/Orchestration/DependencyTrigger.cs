namespace Pleiades.Orchestration;

/// <summary>
/// Identifies which lifecycle event of a dependency's <em>source</em> (the blocking/prerequisite entity)
/// satisfies the dependency (PEP101). Meaningless when the source is a checkpoint, which satisfies on unlock.
/// </summary>
public enum DependencyTrigger
{
	/// <summary>
	/// The dependency is satisfied when the source begins.
	/// </summary>
	OnBegin,

	/// <summary>
	/// The dependency is satisfied when the source finishes (the default).
	/// </summary>
	OnFinish,
}
