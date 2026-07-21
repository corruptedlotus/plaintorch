using Pleiades.Puck;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a Stellar directive: an operational directive with a specific definition and goal.
/// </summary>
/// <remarks>
/// Stellar directives live as table siblings to lunar <see cref="Directive"/> records through a discriminator.
/// They follow the stellar lifecycle <see cref="Directive.Status"/>. Vault storage metadata is inherited from <see cref="Directive"/>.
/// </remarks>
[PuckEntity("stellar-directive")]
[PuckFormat("A{S:8}")]
public sealed class StellarDirective : Directive
{
	/// <summary>
	/// Gets or sets the state of the stellar directive.
	/// </summary>
	[MarkdownField("status")]
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
}
