using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to objectives.
/// </summary>
/// <remarks>
/// Each member declares its <see cref="LifecycleState"/> (PEP103). An objective starts when it is set to <see cref="Polaris"/>,
/// its only started status; coming on board the active onrush sprint commits it to the sprint without starting it. Joining
/// the active sprint or the active Polaris cycle moves an objective to <see cref="Onboard"/> or <see cref="Polaris"/> where
/// nothing blocks it, and leaving either never moves it back (Patch103.3).
/// </remarks>
public enum ObjectiveStatus
{
	/// <summary>
	/// The objective exists but is waiting to be acted on. The objectives' default open status.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	Standby = 0,
	/// <summary>
	/// The objective is temporarily blocked. Still open: it is meant to go on once it is unblocked.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	Blocked = 1,
	/// <summary>
	/// The objective is on board an onrush sprint, to be pursued during it. Not yet started. Named <c>Onrush</c> before
	/// Patch103.3: the <c>OnboardStatus</c> database migration and the v7 vault migration rename what was stored, and a note
	/// that still reads <c>status: Onrush</c> reads as Onboard.
	/// </summary>
	[LifecycleState(LifecycleState.Open)]
	[MarkdownEnumAlias("Onrush")]
	Onboard = 2,
	/// <summary>
	/// The objective is currently part of Polaris execution. Entering this state is what starts it.
	/// </summary>
	[LifecycleState(LifecycleState.Started)]
	Polaris = 3,
	/// <summary>
	/// The objective has been completed. The objectives' default finished status.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Done = 4,
	/// <summary>
	/// The objective has been archived.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Archived = 5,
	/// <summary>
	/// The objective has failed. Deferred: neither started nor finished, and its intrinsic dependency on its directive is
	/// switched off while it stands (PEP102 patch 9).
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	Failed = 6,
}
