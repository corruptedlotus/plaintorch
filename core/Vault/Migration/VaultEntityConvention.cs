namespace Pleiades.Vault.Migration;

/// <summary>
/// Describes how a single vault-backed entity type was stored and read at a particular vault schema version.
/// </summary>
/// <remarks>
/// A convention captures the pieces of storage policy that can change between versions and that a version-specific
/// loader needs in order to reconstruct entity state from disk. Enumeration roots, shape, partitioning, and field
/// mapping are read from the live model catalog and markdown metadata; only the version-variant identity conventions
/// are captured here.
/// </remarks>
/// <param name="EntityType">The CLR entity type this convention applies to.</param>
/// <param name="Mode">The storage mode in effect at the version.</param>
/// <param name="PuckStorage">The PUCK storage form (quiet frontmatter identity vs filename-embedded identity).</param>
/// <param name="PuckFrontMatterKey">The frontmatter key used for quiet PUCK identity at the version.</param>
/// <param name="FilenameSeparator">The separator used between a PUCK token and title in filename-embedded identity.</param>
public sealed record VaultEntityConvention(
	Type EntityType,
	VaultStorageMode Mode,
	VaultPuckStorage PuckStorage,
	string PuckFrontMatterKey,
	string FilenameSeparator)
{
	/// <summary>
	/// Gets the CLR entity name.
	/// </summary>
	public string EntityName => EntityType.Name;
}
