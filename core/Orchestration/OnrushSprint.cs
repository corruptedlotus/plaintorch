using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a short-term sprint that groups focused objectives.
/// </summary>
[PuckEntity("onrush-sprint")]
[PuckFormat("x{I:4:100}")]
[VaultStorage(LocationKey = VaultLocationKeys.Onrush,
		Mode = VaultStorageMode.Enforced,
		Shape = VaultStorageShape.SelfNamedDirectory,
		PuckStorage = VaultPuckStorage.Index
	)]
public sealed class OnrushSprint : PuckNamedEntity
{
	/// <summary>
	/// The reserved identity of the in-planning placeholder sprint — the sprint being shaped before it is begun and
	/// assigned its final date-based identity. It is <c>x0000</c> (the onrush numerator's zero, before the auto-generated
	/// sequence that starts at 100) so it is a legitimate, gate-passing onrush PUCK rather than a bare sentinel the
	/// identity system rejects and the watcher purges.
	/// </summary>
	public const string PlanningPlaceholderId = "x0000";

	/// <summary>
	/// Gets or sets the sprint start date.
	/// </summary>
	[MarkdownField("startDate")]
	public DateOnly? StartDate { get; set; }

	/// <summary>
	/// Gets or sets the sprint end date.
	/// </summary>
	[MarkdownField("endDate")]
	public DateOnly? EndDate { get; set; }

	[InverseProperty(nameof(Objective.OnrushSprint))]
	/// <summary>
	/// Gets the objectives tracked by this sprint.
	/// </summary>
	public List<Objective> Objectives { get; set; } = [];

	[InverseProperty(nameof(ExecutiveOrder.OnrushSprint))]
	/// <summary>
	/// Gets the executive orders that shape how this sprint is moved through.
	/// </summary>
	public List<ExecutiveOrder> ExecutiveOrders { get; set; } = [];

	[InverseProperty(nameof(Checkpoint.OnrushSprint))]
	/// <summary>
	/// Gets the checkpoints this sprint tracks, its milestone among them (PEP102).
	/// </summary>
	public List<Checkpoint> Checkpoints { get; set; } = [];

	/// <summary>
	/// Gets or sets the id of this sprint's milestone checkpoint, created with the sprint (PEP102). Optional
	/// only in the schema; every sprint the application creates has one.
	/// </summary>
	public string? MilestoneCheckpointId { get; set; }

	/// <summary>
	/// Gets or sets this sprint's milestone checkpoint — the single checkpoint that stands for the sprint's
	/// completion, distinct from the others it merely tracks.
	/// </summary>
	public Checkpoint? MilestoneCheckpoint { get; set; }

	/// <summary>
	/// Gets or sets the persisted graph layout for this sprint's dependency canvas: a JSON map of node key to
	/// position. Database-only UI state, never written to the vault, and safe to be absent or stale — a node
	/// with no saved position is simply laid out afresh.
	/// </summary>
	public string? GraphLayout { get; set; }
}
