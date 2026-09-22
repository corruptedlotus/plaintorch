using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a per-occurrence instance of a decree (PEP100): an attention/act item shaped like an
/// executive, created through interaction or proximity (unbound) or by manually adding its decree to a
/// Polaris cycle (bound).
/// </summary>
/// <remarks>
/// Mobility rules: an unbound attentive can be done, skipped, or rescheduled (delayed); a Polaris-bound
/// attentive can only be done, skipped, or moved to another Polaris cycle. Its Celestron reward is
/// predefined on the owning decree and granted on each execution.
/// </remarks>
public sealed class Attentive : ITimeAllocated, IOccurrenceInstance
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the attentive record.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the owning decree identifier.
	/// </summary>
	public required string DecreeId { get; set; }

	[ForeignKey(nameof(DecreeId))]
	[InverseProperty(nameof(Decree.Attentives))]
	/// <summary>
	/// Gets or sets the owning decree.
	/// </summary>
	public Decree? Decree { get; set; }

	/// <summary>
	/// Gets or sets the bound Polaris cycle identifier, when the attentive is Polaris-bound.
	/// </summary>
	public string? PolarisCycleId { get; set; }

	[ForeignKey(nameof(PolarisCycleId))]
	[InverseProperty(nameof(PolarisCycle.Attentives))]
	/// <summary>
	/// Gets or sets the bound Polaris cycle.
	/// </summary>
	public PolarisCycle? PolarisCycle { get; set; }

	/// <summary>
	/// Gets or sets the occurrence's position in time — the mutable current moment plus its granularity,
	/// nominal duration, and zone (PEP111). An unbound attentive can be rescheduled by moving the moment;
	/// <see cref="RecurrenceId"/> keeps the original slot so orbit dedup and the agenda still resolve it.
	/// </summary>
	public Epoch Epoch { get; set; } = new();

	/// <summary>
	/// Gets or sets the original occurrence slot date (iCalendar <c>RECURRENCE-ID</c>). Unlike <see cref="Date"/>
	/// (which is mutable — an unbound attentive can be rescheduled), this stays fixed at the occurrence's
	/// original slot, so orbit dedup and the agenda projection resolve to the same occurrence after a reschedule.
	/// Set at materialization.
	/// </summary>
	public DateOnly RecurrenceDate { get; set; }

	/// <summary>
	/// Gets or sets the original occurrence slot time; <see langword="null"/> for an all-day slot. Together with
	/// <see cref="RecurrenceDate"/> it forms the stable <c>RECURRENCE-ID</c>.
	/// </summary>
	public TimeOnly? RecurrenceTime { get; set; }

	/// <inheritdoc />
	[NotMapped]
	public string RecurrenceOwnerUid => DecreeId;

	/// <inheritdoc />
	[NotMapped]
	public RecurrenceId RecurrenceId => new(RecurrenceDate, RecurrenceTime);

	/// <summary>
	/// Gets or sets the exclusive end date of the occurrence's period for super-day orbit granularities
	/// (week/month/year). A week-born attentive occupies its whole week, so multiple Polaris cycles can
	/// collide with it. Null means a single-day occurrence.
	/// </summary>
	public DateOnly? PeriodEndDate { get; set; }

	/// <summary>
	/// Gets or sets how the attentive was resolved.
	/// </summary>
	public AttentiveResolution Resolution { get; set; } = AttentiveResolution.Pending;

	/// <summary>
	/// Gets or sets the UTC time at which the attentive most recently transitioned to <see cref="AttentiveResolution.Done"/>.
	/// This is cleared when the attentive is no longer done.
	/// </summary>
	public DateTimeOffset? ResolvedOn { get; set; }

	/// <summary>
	/// Gets or sets the optional primary time allocation, expressed as a whole-minute working time unit.
	/// Seeded from the owning decree's default length; overridable per instance.
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

	/// <summary>
	/// Gets or sets the optional timeframe this attentive is affined to. Seeded from the owning decree's college
	/// through timeframe auto-inclusion when added to a cycle, overridable per instance; purely semantic, enforcing
	/// nothing — the same affinity an executive carries.
	/// </summary>
	public long? AffinityTimeframeId { get; set; }

	[ForeignKey(nameof(AffinityTimeframeId))]
	/// <summary>
	/// Gets or sets the affined timeframe.
	/// </summary>
	public Timeframe? AffinityTimeframe { get; set; }

	[NotMapped]
	/// <summary>
	/// Gets a value indicating whether the attentive is bound to a Polaris cycle.
	/// </summary>
	public bool IsBound => PolarisCycleId is not null;
}
