using Pleiades.Diagnostics;
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

	/// <summary>Reports that a filesystem root observer initialized successfully.</summary>
	public void ReportRootInitialized(string root)
		=> Report(WatcherOperations.Root, root, Check(WatcherOperations.RootInitFailed, failed: false));

	/// <summary>Reports that a filesystem root observer failed to initialize.</summary>
	public void ReportRootInitializationFailed(string root, string? detail)
		=> Report(WatcherOperations.Root, root, Check(WatcherOperations.RootInitFailed, failed: true, detail, files: [root]));

	/// <summary>Reports a runtime error raised by a filesystem root observer.</summary>
	public void ReportRootError(string root, string? detail)
		=> Report(WatcherOperations.Root, root, Check(WatcherOperations.RootError, failed: true, detail, files: [root]));

	/// <summary>Reports that a relocation candidate synced successfully.</summary>
	public void ReportRelocationSucceeded(string newPath)
		=> Report(WatcherOperations.Relocation, WatcherOperations.GlobalScope, Check(WatcherOperations.RelocationFailed, failed: false));

	/// <summary>Reports a fatal, watcher-halting failure.</summary>
	public void ReportFatal(string? detail)
		=> Report(WatcherOperations.Process, WatcherOperations.GlobalScope, Check(WatcherOperations.Fatal, failed: true, detail));

	/// <summary>
	/// Reports that the vault or one of its entity roots cannot be reached (tier 2): a whole-of-vault condition the
	/// watcher answers by going to sleep. It is a single error-level issue on the vault-access operation, keyed on the
	/// global scope so that whichever root is currently inaccessible, there is exactly one such status; the offending
	/// path is carried in its files and detail. <see cref="ReportVaultAccessible"/> resolves it once access recovers.
	/// </summary>
	public void ReportVaultInaccessible(string offendingPath, string? detail)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(offendingPath);
		var files = string.Equals(offendingPath, WatcherOperations.GlobalScope, StringComparison.Ordinal)
			? (IReadOnlyList<string>?)null
			: [offendingPath];
		var message = string.IsNullOrWhiteSpace(detail) ? $"'{offendingPath}' is not accessible." : detail;
		Report(WatcherOperations.VaultAccess, WatcherOperations.GlobalScope, Check(WatcherOperations.VaultInaccessible, failed: true, message, files));
	}

	/// <summary>Reports that vault access has been restored, resolving the tier-2 issue however it was raised.</summary>
	public void ReportVaultAccessible()
		=> Report(WatcherOperations.VaultAccess, WatcherOperations.GlobalScope, Pass(WatcherOperations.VaultInaccessible));

	/// <summary>
	/// Reports that discovery threw while passively inspecting a path (tier 1). A passive-read failure is advisory: we
	/// could not read the file this pass, so it is left in place and re-checked. It surfaces at warning severity — it
	/// does not, on its own, degrade health — and the retry sweep keeps re-inspecting the path until it reads cleanly,
	/// which resolves the flag. The underlying cause (in use, permission, malformed) is preserved in the reason code.
	/// </summary>
	public void ReportInspectFailure(string path, Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		var reason = ClassifyOperationalFailure(exception, WatcherOperations.DiscoveryFailed);
		var descriptor = WatcherOperations.Describe(reason);
		var detail = string.IsNullOrWhiteSpace(exception.Message)
			? descriptor.Message
			: $"{descriptor.Message} {exception.Message}";
		Report(WatcherOperations.Reconcile, path, OperationCheck.Fail(reason, OperationSeverity.Warning, detail, files: [path]));
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
			? $"{candidate.Issues.Count} validation issue(s). {firstIssue?.FieldPath}: {firstIssue?.Message}"
			: null;

		// Phase D: the policy decision carries a single structured concern classifying the root cause, so one bad file
		// yields one classified reason. A puck/policy/foreign concern is that reason and subsumes any incidental
		// validation issues; markdown-invalid is raised by an explicit markdown concern or, only when the decision
		// surfaced no concern at all, by the mere presence of raw validation issues (the reporter's markdown floor).
		// All checks are reported every run so the status core resolves whichever was previously raised.
		var concern = candidate.Concern;
		var puckViolation = concern == VaultSyncConcern.PuckViolation;
		var policyViolation = concern == VaultSyncConcern.PolicyViolation;
		var foreignFile = concern == VaultSyncConcern.ForeignFile;
		var markdownInvalid = concern == VaultSyncConcern.MarkdownInvalid
			|| (concern == VaultSyncConcern.None && candidate.Issues.Count > 0);

		Report(
			WatcherOperations.Reconcile,
			path,
			Pass(WatcherOperations.DiscoveryFailed),
			Pass(WatcherOperations.PermissionDenied),
			Pass(WatcherOperations.FileInUse),
			Check(WatcherOperations.MarkdownInvalid, markdownInvalid, validationDetail ?? candidate.SuggestedReason, files: [path], entityId: candidate.PathId),
			Check(WatcherOperations.PuckViolation, puckViolation, candidate.SuggestedReason ?? firstIssue?.Message, files: [path], entityId: candidate.PathId),
			Check(WatcherOperations.PolicyViolation, policyViolation, candidate.SuggestedReason, files: [path], entityId: candidate.PathId),
			Check(WatcherOperations.ForeignFile, foreignFile, candidate.SuggestedReason, files: [path], entityId: candidate.PathId));
	}

	/// <summary>
	/// Reports that a candidate synced successfully. A successful sync clears every <em>actionable</em> reconcile
	/// reason for the path — this resolves any issue an earlier <see cref="ReportInspectCandidate"/> raised, and is the
	/// only resolution path for a purged file (the purge suppresses its own delete through the write barrier, so no
	/// later re-inspection will ever clear the flag; a report that passed only <see cref="WatcherOperations.SyncFailed"/>
	/// would strand it). It deliberately does <em>not</em> clear <see cref="WatcherOperations.ForeignFile"/>: leaving an
	/// unmanaged file in place is itself the successful outcome, and the standing advisory persists until the file is
	/// gone or becomes managed (a clean re-inspection), or the user dismisses it.
	/// </summary>
	public void ReportSyncSucceeded(VaultSyncCandidate candidate)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		Report(WatcherOperations.Reconcile, candidate.AbsolutePath, Array.ConvertAll(SyncClearedReasonCodes, Pass));
	}

	/// <summary>Reports that a candidate's sync execution threw, classified to a concrete cause.</summary>
	public void ReportSyncFailure(VaultSyncCandidate candidate, Exception exception)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		var reason = ClassifyOperationalFailure(exception, WatcherOperations.SyncFailed);
		Report(WatcherOperations.Reconcile, candidate.AbsolutePath, Check(reason, failed: true, exception.Message, files: [candidate.AbsolutePath], entityId: candidate.PathId));
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
	];

	// The subset a successful sync clears: every actionable reason, but NOT foreign-file. A foreign file left in place
	// is the successful outcome, so its standing advisory outlives the sync; only a clean re-inspection (file gone or
	// now managed) or an explicit dismissal clears it.
	private static readonly string[] SyncClearedReasonCodes =
		[.. ReconcileReasonCodes.Where(static reason => reason != WatcherOperations.ForeignFile)];

	/// <summary>Reports every reconcile reason code as passing for a scope, resolving any active reconcile flag on it.</summary>
	private void ReportReconcileHealthy(string path)
		=> Report(WatcherOperations.Reconcile, path, Array.ConvertAll(ReconcileReasonCodes, Pass));

	private void Report(string operationId, string scopeKey, params OperationCheck[] checks)
		=> reporter.Report(new OperationReport(operationId, scopeKey, checks));

	private static OperationCheck Pass(string reasonCode) => OperationCheck.Pass(reasonCode);

	private static OperationCheck Check(string reasonCode, bool failed, string? detail = null, IReadOnlyList<string>? files = null, string? entityId = null)
	{
		if (!failed)
		{
			return OperationCheck.Pass(reasonCode);
		}

		var descriptor = WatcherOperations.Describe(reasonCode);
		var message = string.IsNullOrWhiteSpace(detail)
			? descriptor.Message
			: $"{descriptor.Message} {detail}";
		return OperationCheck.Fail(reasonCode, descriptor.Severity, message, files, entityId);
	}

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
