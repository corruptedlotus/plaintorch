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
	/// Gets or sets how this timeframe auto-includes Polaris workitems (PEP100 patch). Defaults to
	/// <see cref="TimeframeInclusion.None"/>; the criterion for <see cref="TimeframeInclusion.College"/> is
	/// <see cref="AutoInclusionCollege"/>.
	/// </summary>
	public TimeframeInclusion AutoInclusion { get; set; } = TimeframeInclusion.None;

	/// <summary>
	/// Gets or sets the college that drives auto-inclusion when <see cref="AutoInclusion"/> is
	/// <see cref="TimeframeInclusion.College"/> — every executive or reflective whose owning incentive carries this
	/// college is affined to this timeframe on creation. Ignored for other inclusion kinds.
	/// </summary>
	public ObjectiveCollege? AutoInclusionCollege { get; set; }
}
