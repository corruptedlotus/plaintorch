using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a Stellar directive: an operational, lifecycle-driven directive with a specific definition and goal.
/// </summary>
/// <remarks>
/// Stellar directives live as table siblings to <see cref="LunarDirective"/> records through a discriminator and
/// share the <see cref="VaultLocationKeys.Directives"/> vault storage inherited from <see cref="Directive"/>.
/// They follow the stellar lifecycle <see cref="Status"/> and carry optional scheduling dates.
/// </remarks>
[PuckEntity("stellar-directive")]
[PuckFormat("A{S:8}")]
public sealed class StellarDirective : Directive
{
	/// <summary>
	/// Gets or sets the state of the stellar directive.
	/// </summary>
	[MarkdownField("status")]
	[LifecycleStatus]
	public DirectiveStatus Status { get; set; } = DirectiveStatus.Planned;

	/// <summary>
	/// Gets or sets the optional due date.
	/// </summary>
	[MarkdownField("due")]
	public DateOnly? Due { get; set; }

	/// <summary>
	/// Gets or sets the optional directive start date.
	/// </summary>
	[MarkdownField("startDate")]
	public DateOnly? StartDate { get; set; }

	/// <summary>
	/// Gets or sets the optional directive end date.
	/// </summary>
	[MarkdownField("endDate")]
	public DateOnly? EndDate { get; set; }

	/// <summary>
	/// Gets or sets the onrush sprint this directive is a member of, if any (PEP102 patch 10): an onrush tracks stellar
	/// directives the way it tracks objectives. A frontmatter field like the objective's, so a note can name its sprint.
	/// </summary>
	[MarkdownField("onrush")]
	public string? OnrushSprintId { get; set; }

	[ForeignKey(nameof(OnrushSprintId))]
	[InverseProperty(nameof(Orchestration.OnrushSprint.Directives))]
	/// <summary>
	/// Gets or sets the onrush sprint this directive is a member of.
	/// </summary>
	public OnrushSprint? OnrushSprint { get; set; }
}
