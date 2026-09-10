using Microsoft.Extensions.Logging;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Reconciles a discovered candidate — inspect report, sync execution, outcome report — in ONE place, so the startup
/// sweep and live runtime emit identical operation-status issues for the same problem. Both paths funnel through
/// <see cref="ReconcileCandidateAsync"/>, so their issue emission cannot drift (the sweep used to report only the sync
/// outcome and never the inspection concern, so a file left in place — e.g. an unrecognised-identity assertion —
/// surfaced at runtime but not after a sweep).
/// </summary>
/// <remarks>
/// Scoped: it uses the scoped discovery and sync services (one DI scope per event/sweep, mirroring production), plus the
/// singleton status reporter. A per-candidate sync failure is reported and swallowed so one bad candidate never aborts a
/// sweep; only a structural scan failure (from <see cref="VaultMarkdownDiscoveryService.ScanAsync"/>) and cancellation
/// propagate, for the host to classify and retry.
/// </remarks>
public sealed class VaultWatcherReconciler(
	VaultMarkdownDiscoveryService discovery,
	VaultWatcherSyncService syncService,
	WatcherStatusReporter statusReporter,
	ILogger<VaultWatcherReconciler> logger)
{
	/// <summary>
	/// Runs a whole-vault sweep: discover every candidate and reconcile each in startup-priority order. Per-candidate
	/// failures are reported and skipped; a structural discovery failure propagates.
	/// </summary>
	public async Task ReconcileSweepAsync(string origin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);
		var result = await discovery.ScanAsync(origin, cancellationToken);
		logger.LogInformation(
			"Vault discovery completed: {CandidateCount} candidates, {InvalidCount} invalid, {IgnoredCount} ignored.",
			result.Candidates.Count,
			result.InvalidCount,
			result.IgnoredPaths);

		var orderedCandidates = result.Candidates
			.OrderBy(candidate => StartupActionPriority(candidate.SuggestedAction))
			.ThenBy(candidate => candidate.VaultRelativePath, StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var candidate in orderedCandidates)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ReconcileCandidateAsync(candidate, origin, cancellationToken);
		}
	}

	/// <summary>
	/// Reconciles a single path as a live event would: inspect it, then either report it ignored (not a candidate),
	/// report a passive-read failure (tier 1), or reconcile the resolved candidate.
	/// </summary>
	public async Task ReconcilePathAsync(string absolutePath, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);

		VaultSyncCandidate? candidate;
		try
		{
			candidate = await discovery.InspectPathAsync(absolutePath, origin, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			// Tier 1: a passive-read failure is advisory and left to the retry sweep, which keeps re-checking the path
			// from the issue set until it reads cleanly (which resolves the flag).
			statusReporter.ReportInspectFailure(absolutePath, exception);
			logger.LogWarning(exception, "Watcher discovery failed for path '{Path}'; flagged for retry. Processing will continue.", absolutePath);
			return;
		}

		if (candidate is null)
		{
			statusReporter.ReportInspectIgnored(absolutePath);
			logger.LogDebug("Watcher ignored path '{Path}'.", absolutePath);
			return;
		}

		await ReconcileCandidateAsync(candidate, origin, cancellationToken);
	}

	/// <summary>
	/// The single reconcile-and-report path shared by sweep and runtime: report the inspected candidate's concerns,
	/// execute its sync action, then report success (which resolves the actionable concerns it fixed) or, on failure,
	/// the classified failure. Because both callers use this, they emit identical issues for an identical candidate.
	/// </summary>
	public async Task ReconcileCandidateAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);

		if (!candidate.IsValid)
		{
			logger.LogWarning(
				"Watcher candidate '{Path}' for {EntityType} has {IssueCount} validation issue(s); suggested action {Action}.",
				candidate.VaultRelativePath,
				candidate.Model.EntityName,
				candidate.Issues.Count,
				candidate.SuggestedAction);
		}

		statusReporter.ReportInspectCandidate(candidate);

		try
		{
			await syncService.ExecuteAsync(candidate, origin, cancellationToken);
			statusReporter.ReportSyncSucceeded(candidate);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			// The candidate failed to apply; flag it and leave it to the retry sweep, which re-inspects it indefinitely —
			// so if the file is later gone, no longer in violation, or the entity has changed, the recomputed decision
			// resolves or supersedes the flag.
			statusReporter.ReportSyncFailure(candidate, exception);
			logger.LogError(
				exception,
				"Watcher failed to process candidate '{Path}' for {EntityType}; flagged for retry. Processing will continue.",
				candidate.VaultRelativePath,
				candidate.Model.EntityName);
		}
	}

	private static int StartupActionPriority(VaultSyncAction action)
	{
		return action switch
		{
			VaultSyncAction.UpdateFromFile => 0,
			VaultSyncAction.RewriteFromDatabase => 0,
			VaultSyncAction.CreateFromFile => 1,
			VaultSyncAction.PurgeFile => 2,
			VaultSyncAction.Conflict => 3,
			VaultSyncAction.Ignore => 4,
			_ => 5,
		};
	}
}
