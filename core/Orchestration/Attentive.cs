using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a per-occurrence instance of a decree (PEP100/PEP111): an attention/act item shaped like an
/// executive, created through interaction or proximity. An attentive is always an unbound occurrence of its
/// decree's schedule; adding a decree into a Polaris cycle creates an <see cref="Executive"/> instead.
/// </summary>
/// <remarks>
/// An attentive can be done, skipped, or rescheduled (delayed). Its Celestron reward is predefined on the owning
/// decree and granted on each execution. Its row identity, position in time, and RECURRENCE-ID are the shared
/// <see cref="Occurrence"/> members; a week/month/year-born attentive occupies its whole period through the
/// epoch's duration, so multiple Polaris cycles can collide with it.
/// </remarks>
public sealed class Attentive : Occurrence
{
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

	/// <inheritdoc />
	[NotMapped]
	public override string RecurrenceOwnerUid => DecreeId;

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
}
