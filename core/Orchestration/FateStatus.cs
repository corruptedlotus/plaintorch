using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to fate declaratives (PEP100).
/// </summary>
public enum FateStatus
{
	/// <summary>
	/// The fate is active and materializes eventives.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Begin)]
	Active,
	/// <summary>
	/// The fate has been opted out of and no longer materializes eventives.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Finish)]
	OptOut,
	/// <summary>
	/// The fate has been cancelled.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Finish)]
	Cancelled,
}
