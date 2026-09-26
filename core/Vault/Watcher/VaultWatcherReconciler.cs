using Microsoft.Extensions.Logging;
using Pleiades.Resources;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;

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
	VaultStoragePolicyEngine policyEngine,
	WatcherStatusReporter statusReporter,
	ILogger<VaultWatcherReconciler> logger)
{
	private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoDuplicates =
		new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

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

		// One identity map for the whole sweep — computed once from the candidates already in hand — so duplicate
		// detection is O(candidates), not a territory rescan per file.
		var duplicateIdentities = discovery.FindDuplicateIdentities(result.Candidates);

		foreach (var candidate in orderedCandidates)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ReconcileCandidateCoreAsync(candidate, duplicateIdentities, origin, cancellationToken);
		}
	}

	/// <summary>
	/// Reconciles a single path as a live event would: inspect it, then either report it ignored (not a candidate),
	/// report a passive-read failure (tier 1), or reconcile the resolved candidate. A deleted note that names no identity
	/// (an implicit note carries its identity only inside the file) also reconciles, by identity, every begun note that
	/// no longer exists (<see cref="VaultMarkdownDiscoveryService.FindVanishedBoundaryCandidatesAsync"/>).
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

		// The vanished notes' own reconcile is the definitive report for a path it stands at (their issues are scoped to
		// the entity's canonical location), so the path's plain "nothing here" outcome must not clear, say, a sync failure
		// the deletion just raised there — which would reset its retry backoff into a two-second loop.
		if (IsDeletionWithoutIdentity(absolutePath, candidate)
			&& await ReconcileVanishedNotesAsync(absolutePath, origin, cancellationToken))
		{
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
	/// Whether a path is a note (or a folder of notes) that is gone without naming what it held: a missing quiet implicit
	/// note resolves no identity, and a boundary records no path to recover one from.
	/// </summary>
	private static bool IsDeletionWithoutIdentity(string absolutePath, VaultSyncCandidate? candidate)
	{
		var extension = Path.GetExtension(absolutePath);
		return !File.Exists(absolutePath)
			&& !Directory.Exists(absolutePath)
			&& (string.IsNullOrEmpty(extension) || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
			&& (candidate is null || (!candidate.FileExists && string.IsNullOrWhiteSpace(candidate.PathId)));
	}

	/// <summary>
	/// Reconciles every implicit entity whose note is gone — found by identity, the sweep's own check — after a deletion.
	/// Returns whether one of them stands at <paramref name="eventPath"/>.
	/// </summary>
	private async Task<bool> ReconcileVanishedNotesAsync(string eventPath, string origin, CancellationToken cancellationToken)
	{
		IReadOnlyList<VaultSyncCandidate> vanished;
		try
		{
			vanished = await discovery.FindVanishedBoundaryCandidatesAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Watcher could not check which implicit notes are gone after '{Path}' was removed.", eventPath);
			return false;
		}

		var fullEventPath = Path.GetFullPath(eventPath);
		var standsHere = false;
		foreach (var candidate in vanished)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ReconcileCandidateCoreAsync(candidate, NoDuplicates, origin, cancellationToken);
			standsHere |= string.Equals(candidate.AbsolutePath, fullEventPath, StringComparison.OrdinalIgnoreCase);
		}

		return standsHere;
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

		// A lone candidate (a live event) resolves its own identity map by a targeted territory scan, so a runtime
		// reconcile detects — and, once resolved, clears — a duplicate identity the same way the sweep does.
		var duplicateIdentities = await ResolveDuplicateIdentitiesForAsync(candidate, cancellationToken);
		await ReconcileCandidateCoreAsync(candidate, duplicateIdentities, origin, cancellationToken);
	}

	private async Task ReconcileCandidateCoreAsync(
		VaultSyncCandidate candidate,
		IReadOnlyDictionary<string, IReadOnlyList<string>> duplicateIdentities,
		string origin,
		CancellationToken cancellationToken)
	{
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
		ReportIdentityStatus(candidate, duplicateIdentities);
		if (candidate.FileExists && !string.IsNullOrWhiteSpace(candidate.PathId))
		{
			statusReporter.ReportIdentityAsserted(candidate.PathId!, candidate.AbsolutePath);
		}

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
			if (exception is VaultEntityDeleteBlockedException)
			{
				// A refused delete is a standing status, not a failure to retry, and the sync service has already said why.
				return;
			}

			logger.LogError(
				exception,
				"Watcher failed to process candidate '{Path}' for {EntityType}; flagged for retry. Processing will continue.",
				candidate.VaultRelativePath,
				candidate.Model.EntityName);
		}
	}

	/// <summary>
	/// Reports the identity check for a candidate: a duplicate-identity error when more than one file asserts its id,
	/// otherwise a pass that resolves any earlier duplicate flag once the ambiguity is gone. Only live identity
	/// assertions (an identity-driven entity that exists with a resolved id) participate — nothing else can be a
	/// duplicate. Keyed on the identity, so the sweep and a live reconcile raise the one same error for the conflict.
	/// </summary>
	private void ReportIdentityStatus(VaultSyncCandidate candidate, IReadOnlyDictionary<string, IReadOnlyList<string>> duplicateIdentities)
	{
		if (!candidate.FileExists
			|| string.IsNullOrWhiteSpace(candidate.PathId)
			|| !policyEngine.PolicyFor(candidate.Model.Mode).IsIdentityDriven)
		{
			return;
		}

		var id = candidate.PathId!;
		if (duplicateIdentities.TryGetValue(id, out var files) && files.Count > 1)
		{
			var detail = WatcherMessages.Details.DuplicateIdentity(files.Count, string.Join(", ", files));
			statusReporter.ReportDuplicateIdentity(id, files, detail);
		}
		else
		{
			statusReporter.ReportIdentityUnique(id);
		}
	}

	private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ResolveDuplicateIdentitiesForAsync(VaultSyncCandidate candidate, CancellationToken cancellationToken)
	{
		if (!candidate.FileExists
			|| string.IsNullOrWhiteSpace(candidate.PathId)
			|| !policyEngine.PolicyFor(candidate.Model.Mode).IsIdentityDriven)
		{
			return NoDuplicates;
		}

		var files = await discovery.FindFilesAssertingIdentityAsync(candidate.Model, candidate.PathId!, cancellationToken);
		return files.Count > 1
			? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [candidate.PathId!] = files }
			: NoDuplicates;
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
