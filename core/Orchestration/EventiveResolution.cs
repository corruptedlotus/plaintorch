using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines how an eventive occurrence resolves (PEP100). An occurrence that simply happens keeps its pending resolution;
/// passing is temporal, not stateful. Each member declares its <see cref="LifecycleState"/> (PEP103): a resolution only
/// ever opens an occurrence or sets it aside, and its time window starts and finishes a pending one
/// (<see cref="Lifecycle.EventiveLifecycleStateSource"/>).
/// </summary>
public enum EventiveResolution
{
	/// <summary>
	/// The occurrence is pending or has simply happened.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	Pending,
	/// <summary>
	/// The occurrence was missed. Deferred: it satisfies no dependency. The eventives' default deferred resolution.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Missed,
	/// <summary>
	/// The occurrence was cancelled. Deferred: it satisfies no dependency.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Cancelled,

	/// <summary>
	/// The owning fate is opted out of: the occurrence still spawns (so it can be opted back in by setting it
	/// <see cref="Pending"/>) but is hidden from the agenda and set aside, satisfying no dependency. Inherited from the fate's
	/// OptOut status at spawn (PEP100/PEP111).
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	OptOut,
}
