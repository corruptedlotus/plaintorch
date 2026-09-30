using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to directives.
/// </summary>
public enum DirectiveStatus
{
	/// <summary>
	/// The directive has been defined but not yet committed.
	/// </summary>
	Planned,
	/// <summary>
	/// The directive has been committed to active work.
	/// </summary>
	Committed,
	/// <summary>
	/// The directive is currently active.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Begin)]
	Active,
	/// <summary>
	/// The directive has been fulfilled.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Finish)]
	Fulfilled,
	/// <summary>
	/// The directive has concluded and is over.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Finish)]
	Over,
	/// <summary>
	/// The directive has failed. Not a finish: it reads as open and not begun, and its intrinsic dependencies —
	/// to its parent and from its children — are switched off while it stands (PEP102 patch 9).
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Failed)]
	Failed,
}