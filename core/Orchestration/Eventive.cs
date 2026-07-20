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
public sealed class Eventive : ITimeAllocated
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
	/// Gets or sets the occurrence date of the eventive's time specification.
	/// </summary>
	public DateOnly Date { get; set; }

	/// <summary>
	/// Gets or sets the optional start time. An eventive without a start time is all-day.
	/// </summary>
	public TimeOnly? StartTime { get; set; }

	/// <summary>
	/// Gets or sets the optional end time.
	/// </summary>
	public TimeOnly? EndTime { get; set; }

	/// <summary>
	/// Gets or sets how the occurrence resolved. Passing is temporal rather than stateful:
	/// an occurrence that simply happened stays <see cref="EventiveResolution.Pending"/>.
	/// </summary>
	public EventiveResolution Resolution { get; set; } = EventiveResolution.Pending;

	/// <summary>
	/// Gets or sets the optional primary time allocation, expressed as a whole-minute working time unit.
	/// Filled from the owning fate's event duration.
	/// </summary>
	public int? Estimation { get; set; }

	/// <summary>
	/// Gets or sets the optional minimum time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	public int? Minimum { get; set; }

	/// <summary>
	/// Gets or sets the optional maximum time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	public int? Maximum { get; set; }
}
