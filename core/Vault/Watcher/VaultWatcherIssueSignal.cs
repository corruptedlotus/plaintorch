namespace Pleiades.Vault.Watcher;

/// <summary>
/// Describes a watcher issue signal evaluated against a declarative operation criterion.
/// </summary>
public sealed record VaultWatcherIssueSignal(
	VaultWatcherIssueType Type,
	string Criterion,
	string ResolutionCriterion,
	string? ScopeKey = null,
	string? Path = null,
	string? Detail = null)
{
	/// <summary>
	/// Gets the canonical issue key for this signal.
	/// </summary>
	public string IssueKey => string.IsNullOrWhiteSpace(ScopeKey)
		? Type.ToString().ToLowerInvariant()
		: $"{Type.ToString().ToLowerInvariant()}:{ScopeKey}";
}
