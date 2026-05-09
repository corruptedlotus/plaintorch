using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Captures a path-resolved markdown candidate discovered during startup scan or watcher reconciliation.
/// </summary>
public sealed record VaultSyncCandidate(
	string AbsolutePath,
	string VaultRelativePath,
	VaultPathSyncModel Model,
	string? PathId,
	string PathTitle,
	object ParsedModel,
	IReadOnlyList<MarkdownValidationIssue> Issues,
	string BodyHash,
	DateTimeOffset LastWriteUtc,
	VaultSyncAction SuggestedAction,
	string? SuggestedReason = null)
{
	/// <summary>
	/// Gets a value indicating whether the candidate mapped without validation issues.
	/// </summary>
	public bool IsValid => Issues.Count == 0;
}
