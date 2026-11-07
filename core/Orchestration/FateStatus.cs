using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to fate declaratives (PEP100). Each member declares its <see cref="LifecycleState"/>
/// (PEP103): a fate is under way from its creation and is either running or set aside, so it is never open and never
/// finishes. A fate keeps a history of its states (<see cref="LifecycleStatusAttribute.History"/>).
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
}
