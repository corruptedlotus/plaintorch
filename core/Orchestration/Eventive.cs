using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a per-occurrence instance of a fate, shaped like an executive (PEP100). Eventives record occurrences that
/// happen rather than work that gets done.
/// </summary>
/// <remarks>
/// Eventives are never bound to a Polaris cycle; a cycle's 24h inclusion of them is presentational only.
/// Because they are unbound, eventives can be moved (their epoch's moment changed) while their RECURRENCE-ID
/// keeps the original slot for dependency references and orbit dedup. They carry no Celestron reward. Row
/// identity, position in time, and RECURRENCE-ID are the shared <see cref="Occurrence"/> members. An item's due is
/// a <see cref="Terminative"/>, no longer an eventive (PEP103 patch 7), so a fate is the one owner an eventive has.
/// </remarks>
public sealed class Eventive : Occurrence
{
	/// <summary>
	/// Gets or sets the owning fate identifier.
	/// </summary>
	public required string FateId { get; set; }

	[ForeignKey(nameof(FateId))]
	[InverseProperty(nameof(Fate.Eventives))]
	/// <summary>
	/// Gets or sets the owning fate.
	/// </summary>
	public Fate? Fate { get; set; }

	/// <inheritdoc />
	[NotMapped]
	public override string RecurrenceOwnerUid => FateId;

	/// <summary>
	/// Gets or sets how the occurrence resolved. Passing is temporal rather than stateful:
	/// an occurrence that simply happened stays <see cref="EventiveResolution.Pending"/>.
	/// </summary>
	public EventiveResolution Resolution { get; set; } = EventiveResolution.Pending;
}
