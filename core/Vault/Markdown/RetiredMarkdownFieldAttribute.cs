namespace Pleiades.Vault.Markdown;

/// <summary>
/// Declares a frontmatter key that a markdown-backed type once mapped and no longer does. A canonical rewrite keeps a
/// note's unknown frontmatter keys, so without this a retired key would stay in every note that ever carried it,
/// showing a value nothing reads any more; a retired key is dropped instead whenever the note is written.
/// </summary>
/// <remarks>
/// Inherited, so a key retired on a base type is dropped for every derived type. Matching ignores case, as frontmatter
/// parsing does.
/// </remarks>
/// <param name="name">The retired frontmatter field name.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RetiredMarkdownFieldAttribute(string name) : Attribute
{
	/// <summary>
	/// Gets the retired frontmatter field name.
	/// </summary>
	public string Name { get; } = name;
}
