using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Defines a reusable tag and its semantic color identity.
/// </summary>
public sealed class TagDefinition
{
	[Key]
	/// <summary>
	/// Gets or sets the unique tag identifier.
	/// </summary>
	[MarkdownField("id")]
	public required string Id { get; set; }

	/// <summary>
	/// Gets or sets the display title of the tag.
	/// </summary>
	[MarkdownField("title")]
	public required string Title { get; set; }

	/// <summary>
	/// Gets or sets the semantic color assigned to the tag.
	/// </summary>
	[MarkdownField("color")]
	public TagColor Color { get; set; } = TagColor.Light;

	/// <summary>
	/// Gets or sets the optional tag description.
	/// </summary>
	[MarkdownField("description")]
	public string? Description { get; set; }

	[NotMapped]
	/// <summary>
	/// Gets the stable UI token representing the tag color.
	/// </summary>
	public string UiColorToken => TagPalette.GetColorToken(Color);

	[NotMapped]
	/// <summary>
	/// Gets the CSS variable name that a future UI can bind to the tag color.
	/// </summary>
	public string UiCssVariable => TagPalette.GetCssVariable(Color);
}