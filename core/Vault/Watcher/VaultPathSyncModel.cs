using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Describes a vault-backed model that can be detected purely from path and governed by its storage attribute.
/// </summary>
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