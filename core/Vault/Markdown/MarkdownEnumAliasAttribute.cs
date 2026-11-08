namespace Pleiades.Vault.Markdown;

/// <summary>
/// Declares another name a frontmatter value may give an enum member: a name the member went by before it was renamed. A
/// note that still carries it reads as the member, and the note is written with the member's own name the next time it is
/// written. It keeps notes the vault migration of the rename never sees (a template, a note restored from the graveyard)
/// from standing as invalid.
/// </summary>
/// <remarks>
/// Matching ignores case, as enum parsing does. An alias never shadows a current member: it is read only when the value
/// names no member.
/// </remarks>
/// <param name="name">The name the member went by.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public sealed class MarkdownEnumAliasAttribute(string name) : Attribute
{
	/// <summary>
	/// Gets the name the member went by.
	/// </summary>
	public string Name { get; } = name;
}
