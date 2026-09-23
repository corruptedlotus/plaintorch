namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The kind of declarative that owns an occurrence, and therefore which instance table it hardens into: a
/// fate or an objective owns an <see cref="Pleiades.Orchestration.Eventive"/>, a decree owns an
/// <see cref="Pleiades.Orchestration.Attentive"/>.
/// </summary>
public enum OccurrenceOwnerKind
{
	/// <summary>A fate — its occurrences are eventives.</summary>
	Fate,

	/// <summary>A decree — its occurrences are attentives.</summary>
	Decree,

	/// <summary>An objective — its due date is an eventive.</summary>
	Objective,
}

/// <summary>
/// A logical, storage-independent reference to a single declarative occurrence by its owner and RECURRENCE-ID
/// (the iCalendar <c>UID</c> + <c>RECURRENCE-ID</c> identity, PEP101). Addresses an agenda item whether or not
/// it has been hardened into a row, so the same reference resolves a projected occurrence and its hardened
/// twin identically.
/// </summary>
/// <param name="OwnerKind">Whether the owner is a fate, decree, or objective.</param>
/// <param name="OwnerId">The owning declarative's PUCK id.</param>
/// <param name="RecurrenceId">The occurrence's original slot moment (<see cref="Pleiades.Orchestration.Occurrence.RecurrenceId"/>).</param>
public readonly record struct OccurrenceRef(
	OccurrenceOwnerKind OwnerKind,
	string OwnerId,
	DateTime RecurrenceId);
