using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Vault.Media;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a directive-level definition of a portion of time within a Polaris cycle — or within
/// multiple Polaris cycles depending on its Orbit definition (PEP100, originally PEP095).
/// </summary>
/// <remarks>
/// Timeframes do nothing on their own; they simply flag a portion of the day that can be used to clarify
/// affinity. Affinity is purely semantic: executives may name a timeframe as their preferred execution
/// window, and nothing enforces it.
/// </remarks>
public sealed class Timeframe
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the timeframe definition.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the defining directive identifier.
	/// </summary>
	public required string DirectiveId { get; set; }

	[ForeignKey(nameof(DirectiveId))]
	[InverseProperty(nameof(Orchestration.LunarDirective.Timeframes))]
	/// <summary>
	/// Gets or sets the defining directive.
	/// </summary>
	public LunarDirective? Directive { get; set; }

	/// <summary>
	/// Gets or sets the human-readable timeframe title.
	/// </summary>
	public required string Title { get; set; }

	/// <summary>
	/// Gets or sets the start of the flagged portion of the day.
	/// </summary>
	public TimeOnly StartTime { get; set; }

	/// <summary>
	/// Gets or sets the end of the flagged portion of the day.
	/// </summary>
	public TimeOnly EndTime { get; set; }

	/// <summary>
	/// Gets or sets the optional Orbit notation deciding which Polaris cycles the timeframe applies to.
	/// When absent, the timeframe applies to every cycle.
	/// </summary>
	public string? Orbit { get; set; }

	/// <summary>
	/// Gets or sets the timeframe's icon key (PEP100 patch, media system per PEP105): a built-in glyph or lucide
	/// name, or a <c>vault:</c> image in the vault root's shared asset folder. Timeframes keep no asset folder of
	/// their own, so <c>media:</c> self keys have nowhere to resolve. The icon stands in for the Celestron value on
	/// an executive affined to this timeframe.
	/// </summary>
	[Media]
	public string? Icon { get; set; }

	/// <summary>
	/// Gets or sets the resolved companion of <see cref="Icon"/> — its kind and, for custom media, its
	/// vault-relative path. Transient: filled on the way out, never persisted.
	/// </summary>
	[NotMapped]
	public MediaReference? IconMedia { get; set; }

	/// <summary>
	/// Gets or sets how this timeframe auto-includes Polaris workitems (PEP100 patch; Availability per PEP100 patch 2).
	/// Defaults to <see cref="TimeframeInclusion.None"/>. The criteria for <see cref="TimeframeInclusion.College"/> are
	/// <see cref="AutoInclusionColleges"/>. An <see cref="TimeframeInclusion.Availability"/> timeframe carries no
	/// criteria of its own: its parameter lives on the directives that pick it as their
	/// <see cref="Directive.AvailabilityTimeframeId"/>, and it is assigned to workitems whose owning incentive's
	/// directive — or nearest ancestor directive — picked it.
	/// </summary>
	/// <remarks>
	/// Availability takes precedence over College: a new executive or reflective is affined to the nearest directive
	/// availability when its lineage has one, and only otherwise to a College timeframe. Either way the choice is made
	/// once, at creation.
	/// </remarks>
	public TimeframeInclusion AutoInclusion { get; set; } = TimeframeInclusion.None;

	/// <summary>
	/// Gets or sets the colleges that drive auto-inclusion when <see cref="AutoInclusion"/> is
	/// <see cref="TimeframeInclusion.College"/>. They are consulted only when no directive availability applies to the
	/// new executive or reflective (availability precedes college, PEP100 patch 2); then a workitem whose owning
	/// incentive carries one of these colleges is affined, on creation, to the lowest-id College timeframe listing that
	/// college — so several timeframes listing the same college do not all receive it. Ignored for other inclusion
	/// kinds. Stored as JSON.
	/// </summary>
	public List<ObjectiveCollege> AutoInclusionColleges { get; set; } = [];

	/// <summary>
	/// Gets or sets whether the timeframe is exclusive (PEP100 patch 2). Exclusivity is judged among the timeframes
	/// active at a given moment: when any of them is exclusive, only the active exclusive ones are reported and every
	/// active non-exclusive timeframe is dropped. An exclusive timeframe that is not active suppresses nothing.
	/// Defaults to <see langword="false"/>.
	/// </summary>
	public bool Exclusive { get; set; }
}
