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
public sealed class Attentive : ITimeAllocated
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
	/// Gets or sets the occurrence date (the start day of the occurrence's period).
	/// Together with <see cref="Time"/> it forms the occurrence identity for orbit dedup.
	/// </summary>
	public DateOnly Date { get; set; }

	/// <summary>
	/// Gets or sets the optional time of day for the attentive. Sub-day orbit granularities (hour/minute)
	/// fill this from the occurrence instant; unbound attentives may carry any time and date.
	/// </summary>
	public TimeOnly? Time { get; set; }

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

	[NotMapped]
	/// <summary>
	/// Gets a value indicating whether the attentive is bound to a Polaris cycle.
	/// </summary>
	public bool IsBound => PolarisCycleId is not null;
}
