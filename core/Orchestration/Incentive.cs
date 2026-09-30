using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Pleiades.Puck;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents the shared base of the declarative ecosystem (PEP100): objectives and declaratives
/// (fates and decrees) live as table siblings on this base, each with a discriminator. Incentives do not nest:
/// their only structural parent is the owning <see cref="Directive"/>.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Objective), "objective")]
[JsonDerivedType(typeof(Fate), "fate")]
[JsonDerivedType(typeof(Decree), "decree")]
public abstract class Incentive : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the related directive identifier.
	/// </summary>
	public string? DirectiveId { get; set; }

	[ForeignKey(nameof(DirectiveId))]
	[InverseProperty(nameof(Orchestration.Directive.Incentives))]
	/// <summary>
	/// Gets or sets the related directive.
	/// </summary>
	public Directive? Directive { get; set; }

	/// <summary>
	/// Gets the Polaris execution records that work at this incentive (PEP111). Populated for objectives (the day's
	/// plan to work them) and decrees (a cycle's execution of the routine); empty for fates, which are never worked.
	/// </summary>
	[InverseProperty(nameof(Executive.Incentive))]
	public List<Executive> Executives { get; set; } = [];
}
