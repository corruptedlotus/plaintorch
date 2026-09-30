namespace Pleiades.Orchestration;

/// <summary>
/// Tells where a <see cref="Dependency"/> edge comes from (PEP102 patch 9): written by a person, or derived from the
/// structure of the backlog and never stored.
/// </summary>
public enum DependencyOrigin
{
	/// <summary>
	/// A stored edge somebody drew. It has a database identity and can be deleted.
	/// </summary>
	Explicit,

	/// <summary>
	/// An edge the backlog's structure implies, derived when it is read and never stored — for now, a stellar
	/// directive's finish depending on the finish of each of its direct children. It has no identity of its own, cannot
	/// be deleted, and goes away with the relation that implied it.
	/// </summary>
	Intrinsic,
}
