namespace Pleiades.Orchestration;

/// <summary>
/// Identifies which transition of a dependency's <em>target</em> (the blocked/dependant entity) is gated
/// while the dependency is unsatisfied (PEP101). Meaningless when the target is a checkpoint, which has no
/// begin/finish and simply unlocks once its dependencies are met.
/// </summary>
public enum DependencyConstraint
{
	/// <summary>
	/// The target's begin transition is gated (the default).
	/// </summary>
	ToBegin,

	/// <summary>
	/// The target's finish transition is gated.
	/// </summary>
	ToFinish,
}
