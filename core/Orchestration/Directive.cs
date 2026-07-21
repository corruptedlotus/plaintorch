using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a long-running stellar directive that can own incentives and nested directives.
/// PEP100 renames this classic lifecycle-driven kind to "Stellar"; it shares its table with
/// <see cref="LunarDirective"/> through a discriminator.
/// </summary>
[PuckEntity("directive")]
[PuckFormat("A{S:6}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Directives,
	Mode = VaultStorageMode.Freeform,
	Shape = VaultStorageShape.SelfNamedDirectory,
	ParentIdProperty = nameof(ParentDirectiveId),
	ParentEntityType = typeof(Directive))]
[Index(nameof(Codename), IsUnique = true)]
[JsonPolymorphic]
[JsonDerivedType(typeof(StellarDirective), "stellar")]
[JsonDerivedType(typeof(LunarDirective), "lunar")]
public abstract class Directive : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the optional directive codename for cross-referencing in lore and communication.
	/// </summary>
	[MarkdownField("codename")]
	public string? Codename { get; set; }

	/// <summary>
	/// Gets or sets the parent directive identifier when this directive is nested.
	/// </summary>
	public string? ParentDirectiveId { get; set; }

	[ForeignKey(nameof(ParentDirectiveId))]
	[InverseProperty(nameof(Subdirectives))]
	/// <summary>
	/// Gets or sets the parent directive.
	/// </summary>
	public Directive? ParentDirective { get; set; }

	[InverseProperty(nameof(ParentDirective))]
	/// <summary>
	/// Gets the nested child directives.
	/// </summary>
	public List<Directive> Subdirectives { get; set; } = [];

	/// <summary>
	/// Gets or sets the assigned tag identifiers.
	/// </summary>
	[MarkdownField("tags")]
	public List<string> Tags { get; set; } = [];

	[InverseProperty(nameof(Incentive.Directive))]
	/// <summary>
	/// Gets the incentives (objectives and declaratives) attached to this directive.
	/// </summary>
	public List<Incentive> Incentives { get; set; } = [];

	[NotMapped]
	/// <summary>
	/// Gets the objectives attached to this directive.
	/// </summary>
	public IEnumerable<Objective> Objectives => Incentives.OfType<Objective>();
}
