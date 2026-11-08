using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines how an attentive occurrence resolves (PEP100). Each member declares its <see cref="LifecycleState"/> (PEP103):
/// an attentive is open until it is done or skipped, and never under way in between.
/// </summary>
public enum AttentiveResolution
{
	/// <summary>
	/// The attentive has not been resolved yet.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	Pending,
	/// <summary>
	/// The attentive was done, granting the decree's predefined Celestron reward.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Done,
	/// <summary>
	/// The attentive was skipped.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Skipped,
}
