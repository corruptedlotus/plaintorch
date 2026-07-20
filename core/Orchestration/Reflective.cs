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
	public required string Description { get; set; }

	/// <summary>
	/// Gets or sets the owning Polaris cycle identifier.
	/// </summary>
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
	public bool Executed { get; set; }

	/// <summary>
	/// Gets or sets the optional time of day for the reflective (PEP100). Further reflective behavior
	/// belongs to the PEP104 generation engine.
	/// </summary>
	public TimeOnly? Time { get; set; }

	/// <summary>
	/// Gets or sets the originating decree identifier when the reflective was generated through lunar
	/// reflection (PEP100). Manual/drawn reflectives carry no decree.
	/// </summary>
	public string? DecreeId { get; set; }

	[ForeignKey(nameof(DecreeId))]
	[InverseProperty(nameof(Orchestration.Decree.Reflectives))]
	/// <summary>
	/// Gets or sets the originating decree.
	/// </summary>
	public Decree? Decree { get; set; }
}