using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Media;

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

	/// <summary>
	/// Gets or sets the directive's icon key (PEP105): a built-in glyph or lucide name, a <c>media:</c> image in
	/// the directive's own asset folder, or a <c>vault:</c> image in the vault root's shared asset folder. When
	/// absent the banner falls back to the per-kind default glyph.
	/// </summary>
	[MarkdownField("icon")]
	[Media]
	public string? Icon { get; set; }

	/// <summary>
	/// Gets or sets the directive's banner image key (PEP105): a <c>media:</c> image in the directive's own asset
	/// folder or a <c>vault:</c> image in the vault root's shared asset folder, shown as a header image.
	/// </summary>
	[MarkdownField("banner")]
	[Media]
	public string? Banner { get; set; }

	/// <summary>
	/// Gets or sets the resolved companion of <see cref="Icon"/> (PEP105) — its kind and, for custom media, its
	/// vault-relative path. Transient: filled on the way out, never persisted to the database or frontmatter.
	/// </summary>
	[NotMapped]
	public MediaReference? IconMedia { get; set; }

	/// <summary>
	/// Gets or sets the resolved companion of <see cref="Banner"/> (PEP105). Transient: filled on the way out,
	/// never persisted to the database or frontmatter.
	/// </summary>
	[NotMapped]
	public MediaReference? BannerMedia { get; set; }

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
