namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// The four lifecycle states every backlog kind is read in (PEP103): set aside, not yet started, under way and done.
/// </summary>
/// <remarks>
/// <para>
/// The main line runs <see cref="Open"/> → <see cref="Started"/> → <see cref="Finished"/>, and the values are ordered
/// along it, so a later state compares greater. <see cref="Deferred"/> sits below <see cref="Open"/>: it is the side exit
/// out of the main line, not a step along it. A deferred item has neither started nor finished, satisfies no dependency it
/// is the source of, and comes back to the main line when it resumes.
/// </para>
/// <para>
/// Status enums declare the state of each member with <see cref="LifecycleStateAttribute"/>, and kinds whose state is not a
/// plain status value supply it through an <see cref="ILifecycleStateSource"/>. A kind passes through only the states it
/// declares: a fate is under way from its creation and is never open (it runs, is set aside, or passes), a decree never
/// finishes, and a checkpoint can finish or be deferred but never starts.
/// </para>
/// </remarks>
public enum LifecycleState
{
	/// <summary>
	/// The item is set aside: failed, abandoned, opted out, cancelled, skipped or overdue. It is off the main line and
	/// counts as neither started nor finished.
	/// </summary>
	Deferred = -1,

	/// <summary>
	/// The item is on the main line but has not started.
	/// </summary>
	Open = 0,

	/// <summary>
	/// The item is under way.
	/// </summary>
	Started = 1,

	/// <summary>
	/// The item is done. Finishing implies having started.
	/// </summary>
	Finished = 2,
}
