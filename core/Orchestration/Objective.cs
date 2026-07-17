using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an actionable objective attached to directives and optional onrush sprints.
/// Objectives are incentives: they share their table with the fate and decree declaratives (PEP100).
/// </summary>
[PuckFormat("j{S:8}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Objectives,
	Mode = VaultStorageMode.Implicit,
	Shape = VaultStorageShape.SingleFile,
	ParentIdProperty = nameof(DirectiveId),
	ParentEntityType = typeof(Directive),
	PartitionUnder = "Objectives")]
public sealed class Objective : Incentive
{
	/// <summary>
	/// Gets or sets the related onrush sprint identifier.
	/// </summary>
	[MarkdownField("onrush")]
	public string? OnrushSprintId { get; set; }

	[ForeignKey(nameof(OnrushSprintId))]
	[InverseProperty(nameof(OnrushSprint.Objectives))]
	/// <summary>
	/// Gets or sets the related onrush sprint.
	/// </summary>
	public OnrushSprint? OnrushSprint { get; set; }

	/// <summary>
	/// Gets or sets the optional due date.
	/// </summary>
	[MarkdownField("due")]
	public DateOnly? Due { get; set; }

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
	[MarkdownField("starfire")]
	public int CelestronValue { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the objective is enduring or repeating rather than one-and-done.
	/// </summary>
	[MarkdownField("enduring")]
	public bool IsEnduring { get; set; }

	/// <summary>
	/// Gets the execution records linked to this objective.
	/// </summary>
	[InverseProperty(nameof(Executive.Objective))]
	public List<Executive> Executives { get; set; } = [];

	/// <summary>
	/// Gets the eventives materialized from this objective's due date (PEP100).
	/// </summary>
	[InverseProperty(nameof(Eventive.Objective))]
	public List<Eventive> Eventives { get; set; } = [];
}
