using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an actionable objective attached to directives and optional onrush sprints.
/// Objectives are incentives: they share their table with the fate and decree declaratives (PEP100). An objective's due
/// spawns its <see cref="Terminative"/> (<see cref="IDueOwner"/>, PEP103 patch 7).
/// </summary>
[PuckEntity("objective")]
[PuckFormat("j{S:8}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Objectives,
	Mode = VaultStorageMode.Implicit,
	Shape = VaultStorageShape.SingleFile,
	ParentIdProperty = nameof(DirectiveId),
	ParentEntityType = typeof(Directive),
	PartitionUnder = "Objectives")]
public sealed class Objective : Incentive, IDueOwner
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
	/// Gets or sets the optional deadline — a moment plus zone (PEP111), not just a date. Owned by the objective
	/// (EF <c>OwnsOne</c>) and round-tripped to one compact <c>due:</c> frontmatter field.
	/// </summary>
	[MarkdownField("due")]
	public Due? Due { get; set; }

	/// <summary>
	/// Gets or sets the objective college.
	/// </summary>
	[MarkdownField("college")]
	public ObjectiveCollege College { get; set; } = ObjectiveCollege.Unspecified;

	/// <summary>
	/// Gets or sets the current objective workflow status.
	/// </summary>
	[MarkdownField("status")]
	[LifecycleStatus]
	public ObjectiveStatus Status { get; set; } = LifecycleDeclarations.InitialStatus<ObjectiveStatus>();

	/// <summary>
	/// Gets or sets the objective's ledger value.
	/// </summary>
	[MarkdownField("starfire")]
	public int CelestronValue { get; set; }
}
