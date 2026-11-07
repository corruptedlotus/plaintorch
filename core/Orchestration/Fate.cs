using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents the fate declarative (PEP100): an event that cannot be done or undone, but only happen.
/// Fates can be missed or cancelled, be all-day or carry a start and end time (ref. CalDAV vEVENT),
/// and schedule themselves entirely through the Orbit notation (PEP111): a recurring fate uses a recurrence
/// orbit, and a one-off uses a fixed-datetime <c>Z{y/M/d[Th:m]}</c> literal (with a <c>=&lt;dur&gt;</c> span for a
/// timed window). The occurrence's granularity and duration therefore come from the resolved orbit, not from
/// bare date/time fields.
/// </summary>
[PuckEntity("fate")]
[PuckFormat("e{S:8}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Fates,
	Mode = VaultStorageMode.Implicit,
	Shape = VaultStorageShape.SingleFile,
	ParentIdProperty = nameof(DirectiveId),
	ParentEntityType = typeof(Directive),
	PartitionUnder = "Fates")]
public sealed class Fate : Declarative
{
	/// <summary>
	/// Gets or sets the current fate state.
	/// </summary>
	[MarkdownField("status")]
	[LifecycleStatus(History = true)]
	public FateStatus Status { get; set; } = LifecycleDeclarations.InitialStatus<FateStatus>();

	/// <summary>
	/// Gets or sets whether the fate's <see cref="FateStatus.Passed"/> was inferred by the engine rather than selected by hand
	/// (PEP103 I-191). An inferred pass goes back to <see cref="PassedFrom"/> when an orbit change begets an occurrence still
	/// to close; a selected one does not. Only a manual occurrence still to close brings either back. Always
	/// <see langword="false"/> while the fate is not Passed.
	/// </summary>
	/// <remarks>
	/// Database-only state the engine keeps: a note shows <c>status: Passed</c> for both, so the watcher carries this over a
	/// frontmatter sync rather than clearing it. A database rebuilt from the notes reads every pass as selected.
	/// </remarks>
	public bool PassInferred { get; set; }

	/// <summary>
	/// Gets or sets the started status the fate held when it passed — <see cref="FateStatus.Active"/> or
	/// <see cref="FateStatus.OptOut"/> — which bringing it back restores, so an opted-out fate stays hidden. <see langword="null"/>
	/// while it is not Passed, and when it passed from <see cref="FateStatus.Stopped"/>: coming back then restores Active.
	/// </summary>
	/// <remarks>Database-only, carried over a frontmatter sync like <see cref="PassInferred"/>.</remarks>
	public FateStatus? PassedFrom { get; set; }

	/// <summary>
	/// Gets or sets the Orbit scheduling notation that drives the fate's occurrences (PEP111). A recurring fate
	/// uses a recurrence orbit; a one-off uses a fixed-datetime <c>Z{…}</c> literal. <see langword="null"/> means
	/// the fate is unscheduled and materializes no eventives.
	/// </summary>
	[MarkdownField("orbit")]
	public string? Orbit { get; set; }

	/// <summary>
	/// Gets the eventives materialized from this fate (PEP100).
	/// </summary>
	[InverseProperty(nameof(Eventive.Fate))]
	public List<Eventive> Eventives { get; set; } = [];
}
