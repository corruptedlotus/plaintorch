using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a reflective prompt or note attached to a Polaris cycle.
/// </summary>
public sealed class Reflective
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the reflective record.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the reflection description.
	/// </summary>
	[MarkdownField("description")]
	public required string Description { get; set; }

	/// <summary>
	/// Gets or sets the owning Polaris cycle identifier.
	/// </summary>
	[MarkdownField("polarisCycle")]
	public required string PolarisCycleId { get; set; }

	[ForeignKey(nameof(PolarisCycleId))]
	[InverseProperty(nameof(PolarisCycle.Reflectives))]
	/// <summary>
	/// Gets or sets the owning Polaris cycle.
	/// </summary>
	public PolarisCycle? PolarisCycle { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the reflection was completed.
	/// </summary>
	[MarkdownField("executed")]
	public bool Executed { get; set; }
}