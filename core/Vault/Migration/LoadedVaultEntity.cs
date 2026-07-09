namespace Pleiades.Vault.Migration;

/// <summary>
/// Represents a single vault entity reconstructed from disk by a version-specific loader.
/// </summary>
/// <param name="EntityType">The CLR entity type.</param>
/// <param name="Entity">The hydrated CLR entity (identity and mapped fields populated from the file).</param>
/// <param name="Id">The resolved PUCK identity.</param>
/// <param name="Title">The resolved title.</param>
/// <param name="AbsolutePath">The absolute path of the source markdown file read under the stored conventions.</param>
/// <param name="VaultRelativePath">The vault-relative path of the source markdown file.</param>
/// <param name="Body">The markdown body content, preserved verbatim across the migration.</param>
/// <param name="RawFrontMatter">The raw frontmatter key/value view of the source file.</param>
public sealed record LoadedVaultEntity(
	Type EntityType,
	object Entity,
	string Id,
	string Title,
	string AbsolutePath,
	string VaultRelativePath,
	string Body,
	IReadOnlyDictionary<string, string> RawFrontMatter)
{
	/// <summary>
	/// Gets the CLR entity name.
	/// </summary>
	public string EntityName => EntityType.Name;
}
