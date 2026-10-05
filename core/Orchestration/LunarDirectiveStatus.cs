using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the moonlight states available to lunar directives (PEP100). Each member declares its
/// <see cref="LifecycleState"/> (PEP103): a lunar directive is either enforced or set aside, so it is never open and never
/// finishes.
/// </summary>
public enum LunarDirectiveStatus
{
	/// <summary>
	/// The lunar directive exists but is not currently enforced; reflection under it is paused. The lunar directives'
	/// default deferred status.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	OnHold,
	/// <summary>
	/// The lunar directive is actively enforced as a pillar of Project Moonlight.
	/// </summary>
	[LifecycleState(LifecycleState.Started)]
	Active,
	/// <summary>
	/// The lunar directive has gone stale and needs attention or retirement; reflection under it is paused.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Stale,
}
