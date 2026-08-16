using Pleiades.Diagnostics;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Translates watcher pipeline outcomes into operation-status reports (PEP108) and forwards them to the
/// <see cref="OperationStatusReporter"/>. Each watcher stage reports its full check-set for a scope, so the status
/// core raises and resolves flags by diffing — the watcher never marks or clears a flag by hand.
/// </summary>
/// <remarks>
/// The exception- and candidate-classification heuristics here (string sniffing of exception messages and
/// <c>SuggestedReason</c>) are carried over unchanged from the previous issue system to keep this a
/// behaviour-preserving swap. They are the subject of PEP108 phases C (typed failures) and D (structured policy
/// outcomes, with REFACTOR Alpha phase 4), which replace them with structured data.
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

	/// <summary>Reports that discovery threw while inspecting a path, classified to a concrete cause.</summary>
	public void ReportInspectFailure(string path, Exception exception)
	{
		var reason = ClassifyOperationalFailure(exception, WatcherOperations.DiscoveryFailed);
		Report(WatcherOperations.Reconcile, path, Check(reason, failed: true, exception.Message, files: [path]));
	}

	/// <summary>Reports that a path was inspected cleanly but is not a managed candidate: all reconcile checks pass.</summary>
	public void ReportInspectIgnored(string path)
		=> Report(
			WatcherOperations.Reconcile,
			path,
			Pass(WatcherOperations.DiscoveryFailed),
			Pass(WatcherOperations.PermissionDenied),
			Pass(WatcherOperations.FileInUse),
			Pass(WatcherOperations.MarkdownInvalid),
			Pass(WatcherOperations.PuckViolation),
			Pass(WatcherOperations.PolicyViolation));

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
		var puckViolation = HasPuckViolation(candidate);
		var policyViolation = HasPolicyViolation(candidate);

		Report(
			WatcherOperations.Reconcile,
			path,
			Pass(WatcherOperations.DiscoveryFailed),
			Pass(WatcherOperations.PermissionDenied),
			Pass(WatcherOperations.FileInUse),
			Check(WatcherOperations.MarkdownInvalid, candidate.Issues.Count > 0, validationDetail, files: [path], entityId: candidate.PathId),
			Check(WatcherOperations.PuckViolation, puckViolation, puckViolation ? candidate.SuggestedReason ?? firstIssue?.Message : null, files: [path], entityId: candidate.PathId),
			Check(WatcherOperations.PolicyViolation, policyViolation, policyViolation ? candidate.SuggestedReason : null, files: [path], entityId: candidate.PathId));
	}

	/// <summary>Reports that a candidate synced successfully.</summary>
	public void ReportSyncSucceeded(VaultSyncCandidate candidate)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		Report(WatcherOperations.Reconcile, candidate.AbsolutePath, Check(WatcherOperations.SyncFailed, failed: false));
	}

	/// <summary>Reports that a candidate's sync execution threw, classified to a concrete cause.</summary>
	public void ReportSyncFailure(VaultSyncCandidate candidate, Exception exception)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		var reason = ClassifyOperationalFailure(exception, WatcherOperations.SyncFailed);
		Report(WatcherOperations.Reconcile, candidate.AbsolutePath, Check(reason, failed: true, exception.Message, files: [candidate.AbsolutePath], entityId: candidate.PathId));
	}

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

	private static bool HasPuckViolation(VaultSyncCandidate candidate)
	{
		if (!string.IsNullOrWhiteSpace(candidate.SuggestedReason)
			&& candidate.SuggestedReason.Contains("puck", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return candidate.Issues.Any(issue =>
			issue.FieldPath.Contains("id", StringComparison.OrdinalIgnoreCase)
			|| issue.Message.Contains("puck", StringComparison.OrdinalIgnoreCase));
	}

	private static bool HasPolicyViolation(VaultSyncCandidate candidate)
	{
		if (candidate.SuggestedAction is not (VaultSyncAction.Conflict or VaultSyncAction.PurgeFile))
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(candidate.SuggestedReason))
		{
			return false;
		}

		var reason = candidate.SuggestedReason;
		return reason.Contains("policy", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("disallow", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("reject", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("freeform", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("unknown file", StringComparison.OrdinalIgnoreCase);
	}
}
