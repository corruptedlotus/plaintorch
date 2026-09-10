namespace Pleiades.Orchestration;

/// <summary>
/// A materialized occurrence of a recurring declarative — an <see cref="Eventive"/> or an
/// <see cref="Attentive"/> — identified per CalDAV by its owning declarative's UID plus its RECURRENCE-ID.
/// Both instance kinds derive their identity through this interface, so the agenda projection and orbit dedup
/// address either one uniformly and reschedule-independently.
/// </summary>
public interface IOccurrenceInstance
{
	/// <summary>
	/// Gets the owning declarative's UID: the fate or objective id for an eventive, the decree id for an
	/// attentive.
	/// </summary>
	string RecurrenceOwnerUid { get; }

	/// <summary>
	/// Gets the stable RECURRENCE-ID identifying this occurrence within its owner's recurrence set.
	/// </summary>
	RecurrenceId RecurrenceId { get; }
}
