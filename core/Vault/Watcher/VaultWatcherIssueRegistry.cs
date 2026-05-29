using System.Collections.Concurrent;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Tracks non-persistent watcher issues and derives current watcher health.
/// </summary>
public sealed class VaultWatcherIssueRegistry
{
	private sealed record VaultWatcherIssueDefinition(string Category, bool IsCritical, string MessageTemplate);

	private static readonly IReadOnlyDictionary<VaultWatcherIssueType, VaultWatcherIssueDefinition> IssueDefinitions =
		new Dictionary<VaultWatcherIssueType, VaultWatcherIssueDefinition>
		{
			[VaultWatcherIssueType.StartupScan] = new("startup", true, "Startup discovery scan failed and watcher is operating in degraded startup mode."),
			[VaultWatcherIssueType.DrainTick] = new("runtime", true, "Watcher pending-drain tick failed."),
			[VaultWatcherIssueType.Discovery] = new("runtime", true, "Watcher candidate discovery failed for an inspected path."),
			[VaultWatcherIssueType.Sync] = new("runtime", true, "Watcher failed to process a discovered candidate sync action."),
			[VaultWatcherIssueType.FilePermissionDenied] = new("filesystem", true, "Watcher could not access a file due to filesystem permissions."),
			[VaultWatcherIssueType.FileInUse] = new("filesystem", false, "Watcher could not access a file because it is currently in use by another process."),
			[VaultWatcherIssueType.MarkdownValidationError] = new("validation", true, "Watcher detected markdown/frontmatter validation problems for a candidate file."),
			[VaultWatcherIssueType.PuckViolation] = new("identity", true, "Watcher detected a PUCK identity violation for a candidate file."),
			[VaultWatcherIssueType.PolicyViolation] = new("policy", true, "Watcher detected a storage policy violation for a candidate file."),
			[VaultWatcherIssueType.Relocation] = new("runtime", true, "Watcher failed to process a relocation candidate."),
			[VaultWatcherIssueType.RootInitialization] = new("filesystem", true, "Watcher failed to initialize a filesystem root observer."),
			[VaultWatcherIssueType.FilesystemRootError] = new("filesystem", false, "Filesystem watcher reported a root-level runtime error."),
			[VaultWatcherIssueType.Fatal] = new("runtime", true, "Watcher encountered a fatal unhandled exception and stopped."),
		};

	private readonly ConcurrentDictionary<string, VaultWatcherIssue> _issues = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, VaultWatcherCriterionEvaluation> _criteria = new(StringComparer.OrdinalIgnoreCase);
	private volatile VaultWatcherHealthStatus _overrideStatus = VaultWatcherHealthStatus.Ok;

	/// <summary>
	/// Returns the current watcher health state.
	/// </summary>
	public VaultWatcherHealthStatus GetStatus()
	{
		if (_overrideStatus is VaultWatcherHealthStatus.Standby or VaultWatcherHealthStatus.Offline)
		{
			return _overrideStatus;
		}

		return _issues.Values.Any(static issue => issue.IsCritical)
			? VaultWatcherHealthStatus.Issues
			: VaultWatcherHealthStatus.Ok;
	}

	/// <summary>
	/// Gets active tracked issues.
	/// </summary>
	public IReadOnlyList<VaultWatcherIssue> GetIssues()
	{
		return _issues.Values
			.OrderByDescending(issue => issue.IsCritical)
			.ThenByDescending(issue => issue.LastObservedUtc ?? DateTimeOffset.MinValue)
			.ToList();
	}

	/// <summary>
	/// Gets the latest watcher criterion evaluations, including healthy criteria with no active issue.
	/// </summary>
	public IReadOnlyList<VaultWatcherCriterionEvaluation> GetCriteriaStates()
	{
		return _criteria.Values
			.OrderByDescending(state => state.EvaluatedUtc)
			.ToList();
	}

	/// <summary>
	/// Gets the criterion evaluation totals for declarative watcher health inspection.
	/// </summary>
	public (int Total, int Failed) GetCriterionSummary()
	{
		var active = _criteria.Count;
		return (active, active);
	}

	/// <summary>
	/// Observes an operation criterion and updates issue state declaratively based on criterion satisfaction.
	/// </summary>
	public void Observe(VaultWatcherIssueSignal signal, bool criterionSatisfied)
	{
		ArgumentNullException.ThrowIfNull(signal);

		var evaluatedUtc = DateTimeOffset.UtcNow;

		if (criterionSatisfied)
		{
			_criteria.TryRemove(signal.IssueKey, out _);
			Resolve(signal.IssueKey);
			return;
		}

		_criteria[signal.IssueKey] = new VaultWatcherCriterionEvaluation(
			signal.Criterion,
			Satisfied: false,
			evaluatedUtc,
			signal.ScopeKey,
			signal.Path,
			signal.Detail);

		var definition = IssueDefinitions.TryGetValue(signal.Type, out var resolvedDefinition)
			? resolvedDefinition
			: new VaultWatcherIssueDefinition("runtime", true, signal.Type.ToString());

		var message = signal.Detail is null
			? definition.MessageTemplate
			: $"{definition.MessageTemplate} {signal.Detail}";

		_issues.AddOrUpdate(
			signal.IssueKey,
			_ => new VaultWatcherIssue(
				signal.IssueKey,
				signal.Type,
				definition.Category,
				message,
				definition.IsCritical,
				signal.Criterion,
				signal.ResolutionCriterion,
				1,
				signal.Path,
				evaluatedUtc,
				evaluatedUtc),
			(_, current) => current with
			{
				Category = definition.Category,
				Message = message,
				IsCritical = definition.IsCritical,
				Criterion = signal.Criterion,
				ResolutionCriterion = signal.ResolutionCriterion,
				Path = signal.Path,
				LastObservedUtc = evaluatedUtc,
				OccurrenceCount = current.OccurrenceCount + 1,
			});
	}

	/// <summary>
	/// Records or refreshes an issue.
	/// </summary>
	public void Report(string key, string category, string message, bool isCritical, string? path = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		var observedUtc = DateTimeOffset.UtcNow;
		_issues.AddOrUpdate(
			key,
			_ => new VaultWatcherIssue(key, VaultWatcherIssueType.Sync, category, message, isCritical, key, "resolve manually", 1, path, observedUtc, observedUtc),
			(_, current) => current with
			{
				Category = category,
				Message = message,
				IsCritical = isCritical,
				OccurrenceCount = current.OccurrenceCount + 1,
				Path = path,
				LastObservedUtc = observedUtc,
			});
	}

	/// <summary>
	/// Resolves and removes a tracked issue.
	/// </summary>
	public void Resolve(string key)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		_issues.TryRemove(key, out _);
	}

	/// <summary>
	/// Sets an explicit watcher status override.
	/// </summary>
	public void SetOverrideStatus(VaultWatcherHealthStatus status)
	{
		_overrideStatus = status;
	}

	/// <summary>
	/// Clears explicit status override and resumes issue-derived status.
	/// </summary>
	public void ClearOverrideStatus()
	{
		_overrideStatus = VaultWatcherHealthStatus.Ok;
	}
}
