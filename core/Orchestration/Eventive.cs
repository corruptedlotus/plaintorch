using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a per-occurrence instance of a fate — or of an objective's due date — shaped like an
/// executive (PEP100). Eventives record occurrences that happen rather than work that gets done.
/// </summary>
/// <remarks>
/// Eventives are never bound to a Polaris cycle; a cycle's 24h inclusion of them is presentational only.
/// Because they are unbound, eventives can be moved (their epoch's moment changed) while their RECURRENCE-ID
/// keeps the original slot for dependency references and orbit dedup. They carry no Celestron reward. Row
/// identity, position in time, and RECURRENCE-ID are the shared <see cref="Occurrence"/> members.
/// </remarks>
public sealed class Eventive : Occurrence
{
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

	/// <inheritdoc />
	[NotMapped]
	public override string RecurrenceOwnerUid => FateId ?? ObjectiveId ?? string.Empty;

	/// <summary>
	/// Gets or sets how the occurrence resolved. Passing is temporal rather than stateful:
	/// an occurrence that simply happened stays <see cref="EventiveResolution.Pending"/>.
	/// </summary>
	public EventiveResolution Resolution { get; set; } = EventiveResolution.Pending;
}
