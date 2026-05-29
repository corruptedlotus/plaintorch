namespace Pleiades.Vault.Watcher;

/// <summary>
/// Represents a unique watcher issue tracked in-memory for the current runtime instance.
/// </summary>
public sealed record VaultWatcherIssue(
	string Key,
	VaultWatcherIssueType Type,
	string Category,
	string Message,
	bool IsCritical,
	string Criterion,
	string ResolutionCriterion,
	int OccurrenceCount,
	string? Path = null,
	DateTimeOffset? FirstObservedUtc = null,
	DateTimeOffset? LastObservedUtc = null);
