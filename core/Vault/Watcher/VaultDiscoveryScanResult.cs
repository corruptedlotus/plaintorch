namespace Pleiades.Vault.Watcher;

/// <summary>
/// Summarizes a discovery scan over vault-backed markdown files.
/// </summary>
/// <param name="Candidates">The resolved sync candidates produced by the scan.</param>
/// <param name="IgnoredPaths">The count of paths ignored because they did not resolve to managed entities.</param>
/// <param name="MissingPaths">The count of expected paths that were missing at scan time.</param>
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
