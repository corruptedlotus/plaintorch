using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Describes a vault-backed model that can be detected purely from path and governed by its storage attribute.
/// </summary>
/// <param name="EntityType">The CLR entity type associated with this path model.</param>
/// <param name="ScanRoots">The root directories that should be scanned for markdown candidates.</param>
/// <param name="Mode">The storage mode that governs allowed reconciliation actions.</param>
/// <param name="Shape">The storage shape expected for discovered files.</param>
/// <param name="IsCandidatePath">A predicate that determines whether a path is a candidate for this model.</param>
/// <param name="LoadKnownIdsAsync">Loads known identifiers currently stored for this entity type.</param>
public sealed record VaultPathSyncModel(
	Type EntityType,
	IReadOnlyList<string> ScanRoots,
	VaultStorageMode Mode,
	VaultStorageShape Shape,
	Func<string, bool> IsCandidatePath,
	Func<PlainfraContext, CancellationToken, Task<HashSet<string>>> LoadKnownIdsAsync)
{
	/// <summary>
	/// Gets the CLR entity name.
	/// </summary>
	public string EntityName => EntityType.Name;
}