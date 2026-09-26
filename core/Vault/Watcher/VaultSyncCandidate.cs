using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Captures a path-resolved markdown candidate discovered during startup scan or watcher reconciliation.
/// </summary>
/// <param name="AbsolutePath">The absolute filesystem path to the markdown file being reconciled.</param>
/// <param name="VaultRelativePath">The vault-relative path to the markdown file.</param>
/// <param name="Model">The path sync model that classified the candidate.</param>
/// <param name="PathId">The resolved identity parsed from path and/or frontmatter, when available.</param>
/// <param name="PathTitle">The resolved title parsed from path and/or frontmatter.</param>
/// <param name="ParsedModel">The domain model hydrated from path composition and frontmatter.</param>
/// <param name="Issues">Validation issues discovered during candidate hydration.</param>
/// <param name="BodyHash">A deterministic hash of markdown body content (excluding frontmatter).</param>
/// <param name="LastWriteUtc">The file last-write timestamp observed for the candidate.</param>
/// <param name="FileExists">A value indicating whether the candidate path currently exists on disk.</param>
/// <param name="SuggestedAction">The provisional reconciliation action suggested for this candidate.</param>
/// <param name="SuggestedReason">The human-readable reason explaining the suggested action.</param>
/// <param name="Concern">
/// The structured classification of the decision's root concern (PEP108 phase D). The watcher's status reporter reads
/// this instead of parsing <paramref name="SuggestedReason"/>, so one root cause yields one classified issue.
/// </param>
/// <param name="Vanished">
/// Whether the candidate is an entity whose note is gone, found by identity rather than at a path
/// (<see cref="VaultMarkdownDiscoveryService.FindVanishedNoteCandidatesAsync"/>): its <paramref name="AbsolutePath"/> is
/// only the entity's canonical location, so its issues are keyed on the identity.
/// </param>
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
	bool FileExists,
	VaultSyncAction SuggestedAction,
	string? SuggestedReason = null,
	VaultSyncConcern Concern = VaultSyncConcern.None,
	bool Vanished = false)
{
	/// <summary>
	/// Gets a value indicating whether the candidate mapped without validation issues.
	/// </summary>
	public bool IsValid => Issues.Count == 0;
}
