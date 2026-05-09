using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an execution record captured inside a Polaris cycle.
/// </summary>
public sealed class Executive
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the execution record.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the owning Polaris cycle identifier.
	/// </summary>
	[MarkdownField("polarisCycle")]
	public required string PolarisCycleId { get; set; }

	[ForeignKey(nameof(PolarisCycleId))]
	[InverseProperty(nameof(PolarisCycle.Executives))]
	/// <summary>
	/// Gets or sets the owning Polaris cycle.
	/// </summary>
	public PolarisCycle? PolarisCycle { get; set; }

	/// <summary>
	/// Gets or sets the related objective identifier.
	/// </summary>
	[MarkdownField("objective")]
	public string? ObjectiveId { get; set; }

	[ForeignKey(nameof(ObjectiveId))]
	[InverseProperty(nameof(Objective.Executives))]
	/// <summary>
	/// Gets or sets the related objective.
	/// </summary>
	public Objective? Objective { get; set; }

	/// <summary>
	/// Gets or sets the optional title used for one-shot or more specifically named executive work.
	/// </summary>
	[MarkdownField("title")]
	public string? Title { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the action was executed.
	/// </summary>
	[MarkdownField("executed")]
	public bool Executed { get; set; }
}