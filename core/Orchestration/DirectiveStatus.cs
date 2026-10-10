using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to stellar directives. Each member declares its <see cref="LifecycleState"/>
/// (PEP103).
/// </summary>
public enum DirectiveStatus
{
	/// <summary>
	/// The directive has been defined but not yet committed. The directives' default open status.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	Planned,
	/// <summary>
	/// The directive has been committed to active work, but has not started.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	Committed,
	/// <summary>
	/// The directive is currently active.
	/// </summary>
	[LifecycleState(LifecycleState.Started)]
	Active,
	/// <summary>
	/// The directive has been fulfilled. The directives' default finished status.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Fulfilled,
	/// <summary>
	/// The directive has concluded and is over: finished and closed (PEP103 patch 5). It stands only while every dependency it
	/// has holds, its children included; one that gains one that does not — open work arriving under it among them — steps
	/// back to <see cref="Fulfilled"/>, as the backlog state enforcement level decides.
	/// </summary>
	[LifecycleState(LifecycleState.Finished, Closed = true)]
	Over,
	/// <summary>
	/// The directive has failed. Deferred: neither started nor finished, and its intrinsic dependencies — to its parent and
	/// from its children — are switched off while it stands (PEP102 patch 9).
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Failed,
}
