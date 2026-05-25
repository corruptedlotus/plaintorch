using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a long-running directive that can own objectives and nested directives.
/// </summary>
[PuckFormat("A{S:6}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Directives,
	Mode = VaultStorageMode.Freeform,
	Shape = VaultStorageShape.SelfNamedDirectory,
	ParentIdProperty = nameof(ParentDirectiveId),
	ParentEntityType = typeof(Directive))]
[Index(nameof(Codename), IsUnique = true)]
public sealed class Directive : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the optional directive codename for cross-referencing in lore and communication.
	/// </summary>
	[MarkdownField("codename")]
	public string? Codename { get; set; }

	/// <summary>
	/// Gets or sets the parent directive identifier when this directive is nested.
	/// </summary>
	[MarkdownField("parent")]
	public string? ParentDirectiveId { get; set; }

	[ForeignKey(nameof(ParentDirectiveId))]
	[InverseProperty(nameof(Subdirectives))]
	/// <summary>
	/// Gets or sets the parent directive.
	/// </summary>
	public Directive? ParentDirective { get; set; }

	[InverseProperty(nameof(ParentDirective))]
	/// <summary>
	/// Gets the nested child directives.
	/// </summary>
	public List<Directive> Subdirectives { get; set; } = [];

	/// <summary>
	/// Gets or sets the current directive workflow status.
	/// </summary>
	[MarkdownField("status")]
	public DirectiveStatus Status { get; set; } = DirectiveStatus.Planned;

	/// <summary>
	/// Gets or sets the assigned tag identifiers.
	/// </summary>
	[MarkdownField("tags")]
	public List<string> Tags { get; set; } = [];

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

	[InverseProperty(nameof(Objective.Directive))]
	/// <summary>
	/// Gets the objectives attached to this directive.
	/// </summary>
	public List<Objective> Objectives { get; set; } = [];
}
