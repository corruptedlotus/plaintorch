using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a short-term sprint that groups focused objectives.
/// </summary>
[PuckFormat("X{D}")]
[VaultStorage(LocationKey = VaultLocationKeys.Onrush, Shape = VaultStorageShape.SelfNamedDirectory)]
public sealed class OnrushSprint : PuckNamedEntity
{
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
}
