namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Identifies a lifecycle phase an entity's status value can represent, used by the dependency system to
/// decide when a source has "begun"/"finished" and which target transition a constraint gates (PEP101).
/// </summary>
/// <remarks>
/// Phases are declared on status enum members via <see cref="LifecyclePhaseAttribute"/> so no per-entity
/// begin/finish knowledge is hardcoded. A value marked <see cref="Finish"/> is terminal and implies the entity
/// has also begun. A value marked <see cref="Failed"/> is neither: the entity counts as open and not even begun,
/// so it never satisfies an edge it is the source of, and its intrinsic dependencies are switched off (PEP102
/// patch 9).
/// </remarks>
public enum LifecyclePhase
{
	/// <summary>
	/// The entity is actively begun / in progress.
	/// </summary>
	Begin,

	/// <summary>
	/// The entity has reached a terminal (finished) state; this implies it has also begun.
	/// </summary>
	Finish,

	/// <summary>
	/// The entity has failed. It counts as open and not begun — a failed prerequisite keeps blocking what it
	/// explicitly blocks — and it neither holds back nor is held back by an intrinsic dependency.
	/// </summary>
	Failed,
}
