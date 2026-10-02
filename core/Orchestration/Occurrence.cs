using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// The non-hierarchical base of a declarative's materialized occurrences (PEP111): an <see cref="Eventive"/> of a
/// fate or of an objective's due, or an <see cref="Attentive"/> of a decree. It carries what every occurrence
/// shares — its row identity, its position in time (<see cref="Epoch"/>), and its iCalendar identity (owner UID +
/// <see cref="RecurrenceId"/>) — so the agenda projection, orbit dedup, and dependency references address either
/// kind uniformly and reschedule-independently.
/// </summary>
/// <remarks>
/// Non-hierarchical: a plain CLR base, never an EF inheritance hierarchy. It is not part of the model (no
/// <c>DbSet</c>, no navigation typed to it), so each occurrence kind keeps its own table and these members map as
/// that table's own columns. Resolution stays on the subclasses, since the two kinds resolve differently.
/// </remarks>
public abstract class Occurrence
{
	/// <summary>
	/// Gets or sets the database identity for the occurrence record.
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the occurrence's position in time — the mutable current moment plus its granularity, nominal
	/// duration, and zone (PEP111). Moving the occurrence changes the moment, while <see cref="RecurrenceId"/>
	/// keeps its original slot. A super-day (week/month/year) occurrence carries its whole period, resolved on its
	/// declarative's calendar, as the <see cref="Epoch.Duration"/>.
	/// </summary>
	public Epoch Epoch { get; set; } = new();

	/// <summary>
	/// Gets or sets the occurrence's iCalendar <c>RECURRENCE-ID</c>: its original <see cref="Epoch"/> moment,
	/// pinned when the occurrence materializes and never moved by a reschedule. Together with
	/// <see cref="RecurrenceOwnerUid"/> it is the stable identity of this occurrence within its owner's recurrence
	/// set, so orbit dedup, the agenda projection, and dependency references resolve the same occurrence after it
	/// moves. A civil (wall-clock) moment, like <see cref="Epoch.Moment"/>; an all-day slot sits at midnight.
	/// </summary>
	/// <remarks>
	/// <see langword="null"/> for a <b>manual</b> occurrence (see <see cref="IsManual"/>): one added by hand rather than
	/// born of its owner's schedule. Like a standalone iCalendar instance it belongs to no recurrence set, so it has
	/// no projected twin to dedup against and is addressed by its row <see cref="Id"/> alone. Only a fate's eventive
	/// can be manual; a decree's attentive and an objective's due eventive always carry one.
	/// </remarks>
	public DateTime? RecurrenceId { get; set; }

	/// <summary>
	/// Gets whether the occurrence was added by hand rather than generated from its owner's schedule — it carries no
	/// <see cref="RecurrenceId"/>. Server-computed.
	/// </summary>
	[NotMapped]
	public bool IsManual => RecurrenceId is null;

	/// <summary>
	/// Gets the owning declarative's UID: the fate or objective id for an eventive, the decree id for an
	/// attentive.
	/// </summary>
	[NotMapped]
	public abstract string RecurrenceOwnerUid { get; }
}
