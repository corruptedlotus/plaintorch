using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to fate declaratives (PEP100). Each member declares its <see cref="LifecycleState"/>
/// (PEP103): a fate is under way from its creation, is either running, set aside or passed, and is never open. A fate keeps
/// a history of its states (<see cref="LifecycleStatusAttribute.History"/>).
/// </summary>
public enum FateStatus
{
	/// <summary>
	/// The fate is active and materializes eventives. A fate is created Active (PEP103 patch 6).
	/// </summary>
	[LifecycleState(LifecycleState.Started, Initial = true)]
	Active,
	/// <summary>
	/// The fate has been opted out of. It keeps running — Started, and temporally active like an Active fate (PEP103
	/// patch 6) — but its occurrences are stamped OptOut, hidden from the agenda, and harden as time passes only when the
	/// auto-materialise-optout preference is on.
	/// </summary>
	[LifecycleState(LifecycleState.Started)]
	OptOut,
	/// <summary>
	/// The fate has been stopped. Deferred: it generates nothing, takes no occurrence, neither finishes nor satisfies a
	/// dependency. Named <c>Cancelled</c> before PEP103 patch 6: the <c>FateStoppedStatus</c> database migration and the v8
	/// vault migration rename what was stored, and a note that still reads <c>status: Cancelled</c> reads as Stopped.
	/// </summary>
	[LifecycleState(LifecycleState.Deferred)]
	[MarkdownEnumAlias("Cancelled")]
	Stopped,
	/// <summary>
	/// The fate has passed: its finish (PEP103 I-191, user ruling). It is inferred and selected alike.
	/// <list type="bullet">
	/// <item><description><b>Inferred.</b> The engine moves a running fate here once it has fully passed — its orbit yields no
	/// occurrence still to close and every hardened eventive has closed — and back to the started status it left once a
	/// manual occurrence still to close is added or its orbit changes to beget one.</description></item>
	/// <item><description><b>Selected.</b> Picked by hand, it acts like <see cref="Stopped"/>: it generates nothing and an
	/// orbit change leaves it, but a manual occurrence still to close is taken and brings the fate back.</description></item>
	/// </list>
	/// Which of the two it is, and the started status it left, are <see cref="Fate.PassInferred"/> and
	/// <see cref="Fate.PassedFrom"/>. Finished either way: temporally inactive, and the finish an edge from the fate waits
	/// for.
	/// </summary>
	[LifecycleState(LifecycleState.Finished)]
	Passed,
}
