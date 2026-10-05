using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to fate declaratives (PEP100). Each member declares its <see cref="LifecycleState"/>
/// (PEP103): a fate is either running or set aside, so it is never open and never finishes.
/// </summary>
public enum FateStatus
{
	/// <summary>
	/// The fate is active and materializes eventives.
	/// </summary>
	[LifecycleState(LifecycleState.Started)]
	Active,
	/// <summary>
	/// The fate has been opted out of. Deferred: its eventives materialize only when the auto-materialise-optout preference
	/// is on. The fates' default deferred status.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	OptOut,
	/// <summary>
	/// The fate has been cancelled. Deferred: it neither finishes nor satisfies a dependency.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Cancelled,
}
