using Pleiades.Diagnostics;
using Pleiades.Resources;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Translates watcher pipeline outcomes into operation-status reports (PEP108) and forwards them to the
/// <see cref="OperationStatusReporter"/>. Each watcher stage reports its full check-set for a scope, so the status
/// core raises and resolves flags by diffing — the watcher never marks or clears a flag by hand.
/// </summary>
/// <remarks>
/// Classification is fully structural (PEP108 phases C and D): inspect/sync failures are classified from typed
/// exceptions (<see cref="ClassifyOperationalFailure"/>), and an inspected candidate's policy concern is read from the
/// decision's typed <see cref="VaultSyncConcern"/> rather than by sniffing its reason string. The reason string is
/// carried through only as human-readable detail.
/// </remarks>
public sealed class WatcherStatusReporter(OperationStatusReporter reporter)
{
	/// <summary>Reports the outcome of the startup discovery scan.</summary>
	public void ReportStartupScan(bool succeeded, string? detail = null)
		=> Report(WatcherOperations.StartupScan, WatcherOperations.GlobalScope, Check(WatcherOperations.ScanFailed, failed: !succeeded, detail));

	/// <summary>Reports the outcome of a pending-drain tick.</summary>
	public void ReportDrainTick(bool succeeded, string? detail = null)
		=> Report(WatcherOperations.DrainTick, WatcherOperations.GlobalScope, Check(WatcherOperations.TickFailed, failed: !succeeded, detail));

	/// <summary>
	/// Reports that a filesystem root observer initialized successfully. A fresh observer also resolves any error the
	/// root's previous observer raised: that observer is gone, and the span that attached this one began with a sweep.
	/// </summary>
	public void ReportRootInitialized(string root)
		=> Report(WatcherOperations.Root, root, Pass(WatcherOperations.RootInitFailed), Pass(WatcherOperations.RootError));

	/// <summary>Reports that a filesystem root observer failed to initialize.</summary>
	public void ReportRootInitializationFailed(string root, string? detail)
		=> Report(WatcherOperations.Root, root, Check(WatcherOperations.RootInitFailed, failed: true, detail, files: [root], fingerprint: root));

	/// <summary>
	/// Reports a runtime error raised by a filesystem root observer, so changes under the root may have been missed. An
	/// overflowing event buffer leaves the observer running and <see cref="ReportRootRecovered"/> resolves it once the
	/// watcher has re-swept; any other error kills the observer, and a fresh one resolves it
	/// (<see cref="ReportRootInitialized"/>).
	/// </summary>
	public void ReportRootError(string root, string? detail)
		=> Report(WatcherOperations.Root, root, Check(WatcherOperations.RootError, failed: true, detail, files: [root], fingerprint: root));

	/// <summary>Reports that a root does not exist: with nothing to observe, any flag against it resolves.</summary>
	public void ReportRootAbsent(string root)
		=> Report(WatcherOperations.Root, root, Pass(WatcherOperations.RootInitFailed), Pass(WatcherOperations.RootError));

	/// <summary>Reports that a root whose observer errored has been re-swept, resolving its root-error flag.</summary>
	public void ReportRootRecovered(string root)
		=> Report(WatcherOperations.Root, root, Pass(WatcherOperations.RootError));

	/// <summary>
	/// Reports whether the watcher could resolve the roots it observes. Failing to is fatal — with no roots there is no
	/// live observation at all — so the watcher sleeps and retries; a later resolution resolves the flag.
	/// </summary>
	public void ReportRootsResolved(bool succeeded, string? detail = null)
		=> Report(WatcherOperations.Root, WatcherOperations.GlobalScope, Check(WatcherOperations.RootsUnresolved, failed: !succeeded, detail));

	/// <summary>Reports that a relocation candidate synced successfully.</summary>
	public void ReportRelocationSucceeded(string newPath)
		=> Report(WatcherOperations.Relocation, WatcherOperations.GlobalScope, Check(WatcherOperations.RelocationFailed, failed: false));

	/// <summary>
	/// Reports a relocation the fast path applied: the old path is gone, so every reconcile flag it carried resolves, and
	/// the moved candidate is reported at its new path — its content concerns move with the file, exactly as a plain
	/// inspection of the new path would report them.
	/// </summary>
	public void ReportRelocated(string oldPath, VaultSyncCandidate candidate)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
		ArgumentNullException.ThrowIfNull(candidate);
		ReportReconcileHealthy(oldPath);
		ReportInspectCandidate(candidate);
		ReportSyncSucceeded(candidate);
		ReportRelocationSucceeded(candidate.AbsolutePath);
	}

	/// <summary>
	/// Reports a fatal, watcher-halting failure: a vault session that fell through every inner guard. The watcher stays
	/// down until the next activation, which starts from a clean status set.
	/// </summary>
	public void ReportFatal(string? detail)
		=> Report(WatcherOperations.Process, WatcherOperations.GlobalScope, Check(WatcherOperations.Fatal, failed: true, detail));

	/// <summary>
	/// Reports that the vault or one of its entity roots cannot be reached (tier 2): a whole-of-vault condition the
	/// watcher answers by going to sleep. It is a single fatal issue on the vault-access operation — health reads standby
	/// while it stands — keyed on the global scope so that whichever root is currently inaccessible, there is exactly one
	/// such status; the offending path is carried in its files and detail. <see cref="ReportVaultAccessible"/> resolves
	/// it once access recovers.
	/// </summary>
	public void ReportVaultInaccessible(string offendingPath, string? detail)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(offendingPath);
		var files = string.Equals(offendingPath, WatcherOperations.GlobalScope, StringComparison.Ordinal)
			? (IReadOnlyList<string>?)null
			: [offendingPath];
		var message = string.IsNullOrWhiteSpace(detail) ? WatcherMessages.Details.PathNotAccessible(offendingPath) : detail;
		Report(WatcherOperations.VaultAccess, WatcherOperations.GlobalScope, Check(WatcherOperations.VaultInaccessible, failed: true, message, files, fingerprint: offendingPath));
	}

	/// <summary>Reports that vault access has been restored, resolving the tier-2 issue however it was raised.</summary>
	public void ReportVaultAccessible()
		=> Report(WatcherOperations.VaultAccess, WatcherOperations.GlobalScope, Pass(WatcherOperations.VaultInaccessible));

	/// <summary>
	/// Reports that more than one file asserts the same entity identity — an ambiguous duplicate the core will not
	/// silently pick a winner for. The issue is keyed on the <em>identity</em> (not a file path), so the sweep and a
	/// live reconcile raise the one same error for the conflict however they find it, and the conflicting files are
	/// carried in its <paramref name="files"/> for the user to resolve. <see cref="ReportIdentityUnique"/> resolves it
	/// once only one file asserts the identity again.
	/// </summary>
	public void ReportDuplicateIdentity(string entityId, IReadOnlyList<string> files, string? detail = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		Report(WatcherOperations.Identity, entityId, Check(WatcherOperations.DuplicateIdentity, failed: true, detail, files, entityId, Fingerprint([entityId, ..files])));
	}

	/// <summary>Reports that an identity is asserted by a single file, resolving any duplicate-identity flag for it.</summary>
	public void ReportIdentityUnique(string entityId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		Report(WatcherOperations.Identity, entityId, Pass(WatcherOperations.DuplicateIdentity));
	}

	/// <summary>
	/// Reports that discovery threw while passively inspecting a path (tier 1). The file is left in place and the retry
	/// sweep keeps re-inspecting it until it reads cleanly, which resolves the flag. The cause is classified into its
	/// reason — a locked file is a warning, a malformed one an error, a forbidden or otherwise failing read critical — and
	/// carries that reason's one severity, the same as when a sync meets it.
	/// </summary>
	public void ReportInspectFailure(string path, Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		var reason = ClassifyOperationalFailure(exception, WatcherOperations.DiscoveryFailed);
		var detail = string.IsNullOrWhiteSpace(exception.Message) ? null : exception.Message;
		Report(WatcherOperations.Reconcile, path, Check(reason, failed: true, detail, files: [path], fingerprint: exception.GetType().Name));
	}

	/// <summary>Reports that a path was inspected cleanly but is not a managed candidate: all reconcile checks pass.</summary>
	public void ReportInspectIgnored(string path)
		=> ReportReconcileHealthy(path);

	/// <summary>
	/// Reports the quality of an inspected candidate: file access checks pass (discovery succeeded), and the
	/// validation/PUCK/policy checks are evaluated from the candidate.
	/// </summary>
	public void ReportInspectCandidate(VaultSyncCandidate candidate)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		var path = candidate.AbsolutePath;
		var firstIssue = candidate.Issues.FirstOrDefault();
		var validationDetail = candidate.Issues.Count > 0
			? WatcherMessages.Details.ValidationIssues(candidate.Issues.Count, firstIssue?.FieldPath, firstIssue?.Message)
			: null;

		// Phase D: the policy decision carries a single structured concern classifying the root cause, so one bad file
		// yields one classified reason. A puck/policy/foreign concern is that reason and subsumes any incidental
		// validation issues; markdown-invalid is raised by an explicit markdown concern or, only when the decision
		// surfaced no concern at all, by the mere presence of raw validation issues (the reporter's markdown floor).
		// All checks are reported every run so the status core resolves whichever was previously raised.
		// A missing file has no content, so whatever the inspection made of its empty read is not a content problem: a
		// deleted file's content flags resolve here instead of outliving it.
		var concern = candidate.FileExists ? candidate.Concern : VaultSyncConcern.None;
		var puckViolation = concern == VaultSyncConcern.PuckViolation;
		var policyViolation = concern == VaultSyncConcern.PolicyViolation;
		var foreignFile = concern == VaultSyncConcern.ForeignFile;
		var markdownInvalid = concern == VaultSyncConcern.MarkdownInvalid
			|| (concern == VaultSyncConcern.None && candidate.FileExists && candidate.Issues.Count > 0);

		// A content problem the watcher enforced (it rewrote or purged the file) no longer stands, so it is only a warning;
		// one left in the file for the user (a conflict, an ignored or partly synced file) keeps its error severity.
		var contentSeverity = ContentSeverity(candidate.SuggestedAction);

		// What makes this the *same* problem next time: the classified concern, the action it led to, and which fields
		// are wrong (with their offending values) — never the reason's prose, which is localisable and presentational.
		var fingerprint = Fingerprint(
		[
			concern.ToString(),
			candidate.SuggestedAction.ToString(),
			..candidate.Issues
				.Select(static issue => issue.RawValue is null ? $"{issue.FieldPath}:{issue.Code}" : $"{issue.FieldPath}:{issue.Code}={issue.RawValue}")
				.Order(StringComparer.Ordinal),
		]);

		Report(
			WatcherOperations.Reconcile,
			path,
			Pass(WatcherOperations.DiscoveryFailed),
			Pass(WatcherOperations.PermissionDenied),
			Pass(WatcherOperations.FileInUse),
			Check(WatcherOperations.MarkdownInvalid, markdownInvalid, validationDetail ?? candidate.SuggestedReason, files: [path], entityId: candidate.PathId, fingerprint, contentSeverity),
			Check(WatcherOperations.PuckViolation, puckViolation, candidate.SuggestedReason ?? firstIssue?.Message, files: [path], entityId: candidate.PathId, fingerprint, contentSeverity),
			Check(WatcherOperations.PolicyViolation, policyViolation, candidate.SuggestedReason, files: [path], entityId: candidate.PathId, fingerprint, contentSeverity),
			Check(WatcherOperations.ForeignFile, foreignFile, candidate.SuggestedReason, files: [path], entityId: candidate.PathId, fingerprint));
	}

	/// <summary>
	/// Reports that a candidate synced successfully. A successful sync always clears the operational reconcile reasons
	/// for the path (the file could be read and the change applied). It clears the <em>content</em> reasons only when the
	/// sync itself fixed the content — the watcher rewrote the file from the database or purged it. That is the only
	/// resolution path for a purged file (the purge suppresses its own delete through the write barrier, so no later
	/// re-inspection will ever clear the flag). Any other action leaves the content as the user wrote it, so the
	/// inspection's verdict stands until the file is fixed: a conflict is resolved by the user, not by recording it.
	/// It never clears <see cref="WatcherOperations.ForeignFile"/>: leaving an unmanaged file in place is itself the
	/// successful outcome, and the standing error persists until the file is gone or becomes managed (a clean
	/// re-inspection), or the user dismisses it.
	/// </summary>
	public void ReportSyncSucceeded(VaultSyncCandidate candidate)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		var cleared = EnforcesContent(candidate.SuggestedAction) ? EnforcedSyncClearedReasonCodes : OperationalReasonCodes;
		Report(WatcherOperations.Reconcile, candidate.AbsolutePath, Array.ConvertAll(cleared, Pass));
	}

	/// <summary>
	/// Reports that a candidate's sync execution threw, classified to a concrete cause. A delete the database would
	/// refuse (<see cref="VaultEntityDeleteBlockedException"/>) is the standing <see cref="WatcherOperations.DeleteBlocked"/>
	/// status, which the retry sweep leaves alone; anything else is a retryable failure. The two sync-only reasons are
	/// reported as one outcome, so whichever this attempt raised supersedes the other rather than leaving a stale
	/// <see cref="WatcherOperations.SyncFailed"/> to keep re-queuing a path that is now known to be blocked.
	/// </summary>
	public void ReportSyncFailure(VaultSyncCandidate candidate, Exception exception)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentNullException.ThrowIfNull(exception);
		var path = candidate.AbsolutePath;
		if (exception is VaultEntityDeleteBlockedException blocked)
		{
			// The same blocked delete is the same problem across passes and restarts — the entity and which relationships
			// hold it — never the reference counts or the message, so an Instance dismissal holds until the blockers change.
			var fingerprint = Fingerprint(
			[
				blocked.EntityId,
				..blocked.Blockers
					.Select(static blocker => $"{blocker.DependentEntity}.{blocker.ForeignKeyProperty}")
					.Order(StringComparer.Ordinal),
			]);
			Report(
				WatcherOperations.Reconcile,
				path,
				Pass(WatcherOperations.SyncFailed),
				Check(WatcherOperations.DeleteBlocked, failed: true, blocked.Message, files: [path], entityId: candidate.PathId, fingerprint));
			return;
		}

		var reason = ClassifyOperationalFailure(exception, WatcherOperations.SyncFailed);
		Report(
			WatcherOperations.Reconcile,
			path,
			Pass(WatcherOperations.DeleteBlocked),
			Check(reason, failed: true, exception.Message, files: [path], entityId: candidate.PathId, fingerprint: exception.GetType().Name));
	}

	// Every reason code the reconcile operation can raise across inspection and sync. A path that no longer resolves to
	// a candidate reports the whole set as passing so the status core resolves *any* previously-raised reason for the
	// scope, since a reason absent from a report is left untouched (see OperationStatusRegistry). Narrower reports would
	// strand earlier flags — the bug this set closes.
	private static readonly string[] ReconcileReasonCodes =
	[
		WatcherOperations.DiscoveryFailed,
		WatcherOperations.PermissionDenied,
		WatcherOperations.FileInUse,
		WatcherOperations.MarkdownInvalid,
		WatcherOperations.PuckViolation,
		WatcherOperations.PolicyViolation,
		WatcherOperations.ForeignFile,
		WatcherOperations.SyncFailed,
		WatcherOperations.DeleteBlocked,
	];

	// The reasons about reading and applying a file, which any successful sync clears — a refused delete included: the
	// restored note's sync, or the delete that finally goes through once nothing references the entity, resolves it.
	private static readonly string[] OperationalReasonCodes =
	[
		WatcherOperations.DiscoveryFailed,
		WatcherOperations.PermissionDenied,
		WatcherOperations.FileInUse,
		WatcherOperations.SyncFailed,
		WatcherOperations.DeleteBlocked,
	];

	// The reasons a sync that enforced the content (a rewrite or a purge) clears: every reason but foreign-file. A foreign
	// file left in place is the successful outcome, so its standing error outlives the sync; only a clean re-inspection
	// (file gone or now managed) or an explicit dismissal clears it.
	private static readonly string[] EnforcedSyncClearedReasonCodes =
		[.. ReconcileReasonCodes.Where(static reason => reason != WatcherOperations.ForeignFile)];

	/// <summary>Whether an action puts the file's content right itself — rewriting it from the database, or purging it.</summary>
	private static bool EnforcesContent(VaultSyncAction action)
		=> action is VaultSyncAction.RewriteFromDatabase or VaultSyncAction.PurgeFile;

	/// <summary>
	/// The severity of a content reason (invalid markdown, a PUCK or policy violation) for the action it led to: a
	/// warning when the watcher enforced it, since nothing is left broken; otherwise the reason's own error severity
	/// (<see langword="null"/>), since the problem stands in the user's file until they fix it.
	/// </summary>
	private static OperationSeverity? ContentSeverity(VaultSyncAction action)
		=> EnforcesContent(action) ? OperationSeverity.Warning : null;

	/// <summary>Reports every reconcile reason code as passing for a scope, resolving any active reconcile flag on it.</summary>
	private void ReportReconcileHealthy(string path)
		=> Report(WatcherOperations.Reconcile, path, Array.ConvertAll(ReconcileReasonCodes, Pass));

	private void Report(string operationId, string scopeKey, params OperationCheck[] checks)
		=> reporter.Report(new OperationReport(operationId, scopeKey, checks));

	private static OperationCheck Pass(string reasonCode) => OperationCheck.Pass(reasonCode);

	// A failing check carries ONLY the specific detail (the policy's reason, an exception message, a composed line) — never
	// the reason's descriptor message. The descriptor message is a function of the reason code and is resolved where the
	// status is presented (the system API looks it up per request), so the wire carries the two halves separately and a
	// client can show, fold, or hide the detail independently. The detail is presentation only: what identifies *this*
	// problem for an Instance dismissal is the structural, culture-invariant fingerprint composed from facts.
	private static OperationCheck Check(string reasonCode, bool failed, string? detail = null, IReadOnlyList<string>? files = null, string? entityId = null, string? fingerprint = null, OperationSeverity? severity = null)
	{
		if (!failed)
		{
			return OperationCheck.Pass(reasonCode);
		}

		var descriptor = WatcherOperations.Describe(reasonCode);
		return OperationCheck.Fail(reasonCode, severity ?? descriptor.Severity, string.IsNullOrWhiteSpace(detail) ? null : detail, files, entityId, fingerprint);
	}

	/// <summary>Composes a structural fingerprint from its parts (order-significant, unit-separated).</summary>
	private static string Fingerprint(IEnumerable<string?> parts)
		=> string.Join('\u001F', parts.Select(static part => part ?? string.Empty));

	private static string ClassifyOperationalFailure(Exception exception, string fallbackReason)
	{
		if (exception is MarkdownDeserializationException)
		{
			return WatcherOperations.MarkdownInvalid;
		}

		return VaultFileAccessException.TryClassify(exception) switch
		{
			VaultFileAccessKind.PermissionDenied => WatcherOperations.PermissionDenied,
			VaultFileAccessKind.InUse => WatcherOperations.FileInUse,
			_ => fallbackReason,
		};
	}
}
