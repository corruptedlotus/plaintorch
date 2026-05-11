namespace Pleiades.Vault.Markdown;

/// <summary>
/// Marks a property as participating in markdown frontmatter serialization.
/// </summary>
/// <param name="name">The frontmatter field name.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MarkdownFieldAttribute(string name) : Attribute
{
	/// <summary>
	/// Gets the field name written into frontmatter.
	/// </summary>
	public string Name { get; } = name;
}