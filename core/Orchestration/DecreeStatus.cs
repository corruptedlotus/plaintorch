using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to decree declaratives (PEP100). Each member declares its <see cref="LifecycleState"/>
/// (PEP103): a decree is under way from its creation and is either running or set aside, so it is never open and never
/// finishes. A decree keeps a history of its states (<see cref="LifecycleStatusAttribute.History"/>).
/// </summary>
public enum DecreeStatus
{
	/// <summary>
	/// The decree is active and its routine materializes attentives. A decree is created Active (PEP103 patch 6).
	/// </summary>
	[LifecycleState(LifecycleState.Started, Initial = true)]
	Active,
	/// <summary>
	/// The decree has been abandoned: set aside, and it no longer materializes attentives.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Abandoned,
}
