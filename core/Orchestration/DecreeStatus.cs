using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to decree declaratives (PEP100). Each member declares its <see cref="LifecycleState"/>
/// (PEP103): a decree is either running or set aside, so it is never open and never finishes.
/// </summary>
public enum DecreeStatus
{
	/// <summary>
	/// The decree is active and its routine materializes attentives.
	/// </summary>
	[LifecycleState(LifecycleState.Started)]
	Active,
	/// <summary>
	/// The decree has been abandoned and no longer materializes attentives.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Abandoned,
}
