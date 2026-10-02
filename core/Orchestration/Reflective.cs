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
	/// Gets or sets the optional time of day for the reflective (PEP100).
	/// </summary>
	public TimeOnly? Time { get; set; }

	/// <summary>
	/// Gets or sets the originating decree identifier. Every reflective is generated through lunar reflection
	/// (PEP100/PEP104), either by its decree's orbit landing on the cycle's day or by the cycle's draw from the
	/// orbit-less pool, and executing it also grants its decree's per-run reward. The reference is cleared only when
	/// the decree is deleted and the reflective is kept as a record of an ended cycle.
	/// </summary>
	public string? DecreeId { get; set; }

	[ForeignKey(nameof(DecreeId))]
	[InverseProperty(nameof(Orchestration.Decree.Reflectives))]
	/// <summary>
	/// Gets or sets the originating decree.
	/// </summary>
	public Decree? Decree { get; set; }

	/// <summary>
	/// Gets or sets the optional timeframe this reflective is affined to (PEP100 patch). Seeded automatically at cycle
	/// begin through timeframe auto-inclusion — the originating decree's nearest directive availability, else its
	/// college (PEP100 patch 2) — and never re-resolved afterwards; purely semantic, enforcing nothing.
	/// </summary>
	public long? AffinityTimeframeId { get; set; }

	[ForeignKey(nameof(AffinityTimeframeId))]
	/// <summary>
	/// Gets or sets the affined timeframe.
	/// </summary>
	public Timeframe? AffinityTimeframe { get; set; }
}