using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines how a <see cref="Terminative"/> resolves (PEP103 patch 7). Only the state engine sets it, from its origin's
/// lifecycle: a terminative is pending while its origin's due acts, and resolved once the origin is finished or set aside
/// (<see cref="TemporalActivation.DueActs"/>). Each member declares its <see cref="LifecycleState"/>.
/// </summary>
public enum TerminativeResolution
{
	/// <summary>
	/// The origin's due still acts: the origin is open or started (or, for a checkpoint, still locked).
	/// </summary>
	[LifecycleState(LifecycleState.Open, Initial = true)]
	Pending,
	/// <summary>
	/// The origin is finished or set aside, so its due acts no more. Taking the origin up again makes the terminative pending
	/// again.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Resolved,
}
