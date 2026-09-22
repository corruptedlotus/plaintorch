using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a per-occurrence instance of a fate — or of an objective's due date — shaped like an
/// executive (PEP100). Eventives record occurrences that happen rather than work that gets done.
/// </summary>
/// <remarks>
/// Eventives are never bound to a Polaris cycle; a cycle's 24h inclusion of them is presentational only.
/// Because they are unbound, eventives can be moved (their time specification changed). They carry no
/// Celestron reward.
/// </remarks>
public sealed class Eventive : IOccurrenceInstance
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the eventive record.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the owning fate identifier, for fate-born eventives.
	/// </summary>
	public string? FateId { get; set; }

	[ForeignKey(nameof(FateId))]
	[InverseProperty(nameof(Fate.Eventives))]
	/// <summary>
	/// Gets or sets the owning fate.
	/// </summary>
	public Fate? Fate { get; set; }

	/// <summary>
	/// Gets or sets the owning objective identifier, for due-date-born eventives.
	/// </summary>
	public string? ObjectiveId { get; set; }

	[ForeignKey(nameof(ObjectiveId))]
	[InverseProperty(nameof(Objective.Eventives))]
	/// <summary>
	/// Gets or sets the owning objective.
	/// </summary>
	public Objective? Objective { get; set; }

	/// <summary>
	/// Gets or sets the occurrence's position in time — the moment plus its granularity, nominal duration
	/// (the event window), and zone (PEP111). An eventive can be moved by changing the moment, while
	/// <see cref="RecurrenceId"/> keeps the original slot for dependency references and orbit dedup.
	/// </summary>
	public Epoch Epoch { get; set; } = new();

	/// <summary>
	/// Gets or sets the original occurrence slot date (iCalendar <c>RECURRENCE-ID</c>) that identifies this
	/// occurrence within its owner's recurrence (PEP101). Unlike <see cref="Date"/> (which is mutable — an
	/// eventive can be moved), this stays fixed at the occurrence's original <c>DTSTART</c>, so dependency
	/// references and orbit dedup resolve to the same occurrence after a reschedule. Set at materialization.
	/// </summary>
	public DateOnly RecurrenceDate { get; set; }

	/// <summary>
	/// Gets or sets the original occurrence slot time for a timed occurrence; <see langword="null"/> for an
	/// all-day slot. Together with <see cref="RecurrenceDate"/> it forms the stable <c>RECURRENCE-ID</c>.
	/// </summary>
	public TimeOnly? RecurrenceTime { get; set; }

	/// <inheritdoc />
	[NotMapped]
	public string RecurrenceOwnerUid => FateId ?? ObjectiveId ?? string.Empty;

	/// <inheritdoc />
	[NotMapped]
	public RecurrenceId RecurrenceId => new(RecurrenceDate, RecurrenceTime);

	/// <summary>
	/// Gets or sets how the occurrence resolved. Passing is temporal rather than stateful:
	/// an occurrence that simply happened stays <see cref="EventiveResolution.Pending"/>.
	/// </summary>
	public EventiveResolution Resolution { get; set; } = EventiveResolution.Pending;
}
