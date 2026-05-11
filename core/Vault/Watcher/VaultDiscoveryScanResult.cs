namespace Pleiades.Vault.Watcher;

/// <summary>
/// Summarizes a discovery scan over vault-backed markdown files.
/// </summary>
public sealed record VaultDiscoveryScanResult(
	IReadOnlyList<VaultSyncCandidate> Candidates,
	int IgnoredPaths = 0,
	int MissingPaths = 0)
{
	/// <summary>
	/// Gets the number of candidates containing one or more validation issues.
	/// </summary>
	public int InvalidCount => Candidates.Count(candidate => !candidate.IsValid);
}
