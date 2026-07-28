using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Pleiades.Puck;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents the shared base of the declarative ecosystem (PEP100): objectives and declaratives
/// (fates and decrees) live as table siblings on this base, each with a discriminator.
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
	/// Gets or sets the optional parent incentive identifier.
	/// </summary>
	/// <remarks>
	/// PEP100 parenting rules: objectives may parent objectives (subtasks) or specify a fate as their parent;
	/// fates may parent fates; decrees are exempt from the parent system entirely. The kind rules are enforced
	/// by the application layer.
	/// </remarks>
	public string? ParentIncentiveId { get; set; }

	[ForeignKey(nameof(ParentIncentiveId))]
	[InverseProperty(nameof(ChildIncentives))]
	/// <summary>
	/// Gets or sets the parent incentive.
	/// </summary>
	public Incentive? ParentIncentive { get; set; }

	[InverseProperty(nameof(ParentIncentive))]
	/// <summary>
	/// Gets the child incentives parented under this incentive.
	/// </summary>
	public List<Incentive> ChildIncentives { get; set; } = [];
}
