using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to objectives.
/// </summary>
/// <remarks>
/// An objective begins when it is set to <see cref="Polaris"/> (PEP101): that is its only begin phase. Taking it into
/// an onrush sprint commits it to the sprint without beginning it.
/// </remarks>
public enum ObjectiveStatus
{
	/// <summary>
	/// The objective exists but is waiting to be acted on.
	/// </summary>
	Standby = 0,
	/// <summary>
	/// The objective is temporarily blocked.
	/// </summary>
	Blocked = 1,
	/// <summary>
	/// The objective is tracked by an onrush sprint, to be pursued during it. Not yet begun.
	/// </summary>
	Onrush = 2,
	/// <summary>
	/// The objective is currently part of Polaris execution. Entering this state is what begins it.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Begin)]
	Polaris = 3,
	/// <summary>
	/// The objective has been completed.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Finish)]
	Done = 4,
	/// <summary>
	/// The objective has been archived.
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Finish)]
	Archived = 5,
	/// <summary>
	/// The objective has failed. Not a finish: it reads as open and not begun, and its intrinsic dependency on its
	/// directive is switched off while it stands (PEP102 patch 9).
	/// </summary>
	[LifecyclePhase(LifecyclePhase.Failed)]
	Failed = 6,
}