namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The kind of declarative that owns an occurrence, and therefore which instance table it hardens into: a
/// fate owns an <see cref="Pleiades.Orchestration.Eventive"/>, a decree owns an
/// <see cref="Pleiades.Orchestration.Attentive"/>. An item's due owns a <see cref="Pleiades.Orchestration.Terminative"/>
/// (PEP103 patch 7), which its origin alone identifies, so it needs no reference of this shape.
/// </summary>
public enum OccurrenceOwnerKind
{
	/// <summary>A fate — its occurrences are eventives.</summary>
	Fate,

	/// <summary>A decree — its occurrences are attentives.</summary>
	Decree,
}

/// <summary>
/// A logical, storage-independent reference to a single declarative occurrence by its owner and RECURRENCE-ID
/// (the iCalendar <c>UID</c> + <c>RECURRENCE-ID</c> identity, PEP101). Addresses an agenda item whether or not
/// it has been hardened into a row, so the same reference resolves a projected occurrence and its hardened
/// twin identically.
/// </summary>
/// <param name="OwnerKind">Whether the owner is a fate or a decree.</param>
/// <param name="OwnerId">The owning declarative's PUCK id.</param>
/// <param name="RecurrenceId">The occurrence's original slot moment (<see cref="Pleiades.Orchestration.Occurrence.RecurrenceId"/>).</param>
public readonly record struct OccurrenceRef(
	OccurrenceOwnerKind OwnerKind,
	string OwnerId,
	DateTime RecurrenceId);
