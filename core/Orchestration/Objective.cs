using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an actionable objective attached to directives and optional onrush sprints.
/// </summary>
[PuckFormat("j{S:8}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Objectives,
	Mode = VaultStorageMode.Synced,
	Shape = VaultStorageShape.SingleFile,
	ParentIdProperty = nameof(DirectiveId),
	ParentEntityType = typeof(Directive),
	PartitionUnder = "Objectives")]
public sealed class Objective : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the related directive identifier.
	/// </summary>
	[MarkdownField("directive")]
	public string? DirectiveId { get; set; }

	[ForeignKey(nameof(DirectiveId))]
	[InverseProperty(nameof(Directive.Objectives))]
	/// <summary>
	/// Gets or sets the related directive.
	/// </summary>
	public Directive? Directive { get; set; }

	/// <summary>
	/// Gets or sets the related onrush sprint identifier.
	/// </summary>
	[MarkdownField("onrushSprint")]
	public string? OnrushSprintId { get; set; }

	[ForeignKey(nameof(OnrushSprintId))]
	[InverseProperty(nameof(OnrushSprint.Objectives))]
	/// <summary>
	/// Gets or sets the related onrush sprint.
	/// </summary>
	public OnrushSprint? OnrushSprint { get; set; }

	/// <summary>
	/// Gets or sets the objective college.
	/// </summary>
	[MarkdownField("college")]
	public ObjectiveCollege College { get; set; } = ObjectiveCollege.Unspecified;

	/// <summary>
	/// Gets or sets the current objective workflow status.
	/// </summary>
	[MarkdownField("status")]
	public ObjectiveStatus Status { get; set; } = ObjectiveStatus.Standby;

	/// <summary>
	/// Gets or sets the objective's ledger value.
	/// </summary>
	[MarkdownField("celestronValue")]
	public int CelestronValue { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the objective is enduring or repeating rather than one-and-done.
	/// </summary>
	[MarkdownField("enduring")]
	public bool IsEnduring { get; set; }

	[InverseProperty(nameof(Executive.Objective))]
	/// <summary>
	/// Gets the execution records linked to this objective.
	/// </summary>
	public List<Executive> Executives { get; set; } = [];
}
