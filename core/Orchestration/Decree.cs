using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents the decree declarative (PEP100): an enduring objective that controls the flow of routines,
/// laws, and requirements, primarily by specifying Orbits that define routines.
/// </summary>
/// <remarks>
/// Decrees are event-like: they are never acted on directly; each occurrence materializes an
/// <see cref="Attentive"/> instead. Decrees are exempt from the incentive parent system.
/// </remarks>
[PuckEntity("decree")]
[PuckFormat("r{S:8}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Decrees,
	Mode = VaultStorageMode.Implicit,
	Shape = VaultStorageShape.SingleFile,
	ParentIdProperty = nameof(DirectiveId),
	ParentEntityType = typeof(Directive),
	PartitionUnder = "Decrees")]
public sealed class Decree : Declarative
{
	/// <summary>
	/// Gets or sets the current decree state.
	/// </summary>
	[MarkdownField("status")]
	public DecreeStatus Status { get; set; } = DecreeStatus.Active;

	/// <summary>
	/// Gets or sets the optional Orbit scheduling notation that defines the decree's routine.
	/// Orbits always resolve to day granularity.
	/// </summary>
	[MarkdownField("orbit")]
	public string? Orbit { get; set; }

	/// <summary>
	/// Gets or sets the optional default length in whole minutes, which seeds the time allocation of the
	/// attentives this decree materializes. The seeded value can be overridden per attentive.
	/// </summary>
	[MarkdownField("defaultLength")]
	public int? DefaultLength { get; set; }

	/// <summary>
	/// Gets or sets the predefined Celestron reward granted on each attentive execution.
	/// This value cannot be overridden per attentive.
	/// </summary>
	[MarkdownField("activeCelestron")]
	public int ActiveCelestron { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the decree participates in daily reflective generation.
	/// Only meaningful for decrees living inside a Moonlight (lunar) directive hierarchy; the generation
	/// engine itself belongs to PEP104.
	/// </summary>
	[MarkdownField("reflect")]
	public bool Reflect { get; set; }

	/// <summary>
	/// Gets or sets the decree college.
	/// </summary>
	[MarkdownField("college")]
	public ObjectiveCollege College { get; set; } = ObjectiveCollege.Unspecified;

	/// <summary>
	/// Gets the attentives materialized from this decree (PEP100).
	/// </summary>
	[InverseProperty(nameof(Attentive.Decree))]
	public List<Attentive> Attentives { get; set; } = [];

	/// <summary>
	/// Gets the reflectives generated from this decree's lunar reflection (PEP100/PEP104).
	/// </summary>
	[InverseProperty(nameof(Reflective.Decree))]
	public List<Reflective> Reflectives { get; set; } = [];
}
