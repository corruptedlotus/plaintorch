using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the moonlight states available to lunar directives (PEP100). Each member declares its
/// <see cref="LifecycleState"/> (PEP103 patch 6): a lunar directive has no ordinary lifecycle. It is created Active, already
/// under way, and moves at will between Active, On Hold (set aside) and Stale (finished), keeping a history of the states
/// it enters (<see cref="LifecycleStatusAttribute.History"/>).
/// </summary>
/// <remarks>
/// A lunar directive's status governs two things (user rulings, PEP103 patch 6): reflection runs under a lunar directive that
/// is Active, its ancestors included, and is paused under one On Hold or Stale; and its own timeframes act until it is Stale
/// (<see cref="TemporalActivation.TimeframesAct"/>), so a Stale one's timeframes are never active and lend no availability or
/// affinity. Nothing else in its hierarchy follows its status: the attentives of its decrees that do not reflect carry on.
/// </remarks>
public enum LunarDirectiveStatus
{
	/// <summary>
	/// The lunar directive is on hold: set aside, and reflection under it is paused.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	OnHold,
	/// <summary>
	/// The lunar directive is actively enforced as a pillar of Project Moonlight. A lunar directive is created Active.
	/// </summary>
	[LifecycleState(LifecycleState.Started, Initial = true)]
	Active,
	/// <summary>
	/// The lunar directive has gone stale: finished, and reflection under it is paused. It can be taken up again.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Stale,
}
