namespace Pleiades.Vault.Watcher;

/// <summary>
/// Represents a single declarative watcher criterion evaluation.
/// </summary>
public sealed record VaultWatcherCriterionEvaluation(
	string Criterion,
	bool Satisfied,
	DateTimeOffset EvaluatedUtc,
	string? ScopeKey = null,
	string? Path = null,
	string? Detail = null);
