using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The watcher adopts the operation-status core (PEP108 phase B): each pipeline stage reports a check-set that the
/// status core diffs into raise/resolve transitions, replacing the hand-marked issue registry. The system API keeps
/// its existing wire contract, and a locked file now surfaces as a first-class suspension.
/// </summary>
public sealed class WatcherStatusTests : VaultTestBase
{
	private VaultSyncCandidate Candidate(
		string relativePath,
		VaultSyncAction action = VaultSyncAction.UpdateFromFile,
		string? reason = null,
		IReadOnlyList<MarkdownValidationIssue>? issues = null)
	{
		var model = Vault.GetSingleton<VaultPathSyncModelCatalog>().GetModels().First(item => item.EntityType == typeof(Objective));
		return new VaultSyncCandidate(
			Vault.AbsolutePath(relativePath),
			relativePath,
			model,
			PathId: "j00000001",
			PathTitle: "Ship it",
			ParsedModel: new object(),
			Issues: issues ?? [],
			BodyHash: "hash",
			LastWriteUtc: DateTimeOffset.UtcNow,
			FileExists: true,
			SuggestedAction: action,
			SuggestedReason: reason);
	}

	[Fact]
	public void Policy_violation_candidate_raises_then_a_clean_inspection_resolves_it()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md", VaultSyncAction.Conflict, "freeform ownership disallowed in a managed root"));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.PolicyViolation, status.ReasonCode);
		Assert.Equal(OperationSeverity.Error, status.Severity);
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());

		// The same path, later inspected clean, resolves the flag — no hand-written un-flagging.
		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md"));
		Assert.Empty(registry.GetActiveStatuses());
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact]
	public void Locked_file_sync_failure_surfaces_as_a_suspension()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		// A sharing-violation IOException (classified by HResult, not message text).
		watcher.ReportSyncFailure(
			Candidate("Objectives/Locked.md"),
			new IOException("locked", unchecked((int)0x80070020)));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.FileInUse, status.ReasonCode);
		Assert.Equal(OperationSeverity.Suspended, status.Severity);
		Assert.Equal(OperationHealth.Suspended, registry.GetHealth());
	}

	[Fact]
	public void Typed_permission_failure_maps_to_permission_denied()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectFailure(
			Vault.AbsolutePath("Objectives/Denied.md"),
			new VaultFileAccessException(VaultFileAccessKind.PermissionDenied, "Objectives/Denied.md", new UnauthorizedAccessException()));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.PermissionDenied, status.ReasonCode);
		Assert.Equal(OperationSeverity.Error, status.Severity);
	}

	[Fact]
	public void Hard_deserialization_failure_maps_to_markdown_invalid_by_type()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectFailure(
			Vault.AbsolutePath("Objectives/Broken.md"),
			new MarkdownDeserializationException("Failed to deserialize markdown into 'Objective'."));

		Assert.Equal(WatcherOperations.MarkdownInvalid, Assert.Single(registry.GetActiveStatuses()).ReasonCode);
	}

	[Fact]
	public void Validation_issue_maps_to_a_critical_markdown_status()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectCandidate(Candidate(
			"Objectives/Broken.md",
			issues: [new MarkdownValidationIssue("status", "Unknown enum value.")]));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.MarkdownInvalid, status.ReasonCode);
		Assert.Equal(OperationSeverity.Error, status.Severity);
	}

	[Fact]
	public void Sync_success_resolves_the_inspection_issue_a_purge_would_otherwise_strand()
	{
		// Regression for the "errors linger after purge" report. A disallowed file is inspected (raising a policy
		// violation) and then purged. The purge suppresses its own delete through the write barrier, so no later
		// re-inspection will ever report the file gone — the sync-success report is the only thing that can resolve
		// the flag, and it must clear the *whole* reconcile check-set, not just SyncFailed.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		var candidate = Candidate("Objectives/Stray.md", VaultSyncAction.PurgeFile, "Unknown file is disallowed by enforced storage policy.");
		watcher.ReportInspectCandidate(candidate);
		Assert.Equal(WatcherOperations.PolicyViolation, Assert.Single(registry.GetActiveStatuses()).ReasonCode);

		watcher.ReportSyncSucceeded(candidate);

		Assert.Empty(registry.GetActiveStatuses());
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
		// Resolved, not merely never-raised: the transition is recorded.
		Assert.Contains(registry.GetRecentResolved(), transition => transition.ReasonCode == WatcherOperations.PolicyViolation);
	}

	[Fact]
	public void A_clean_reinspection_of_a_vanished_file_resolves_its_raised_issue()
	{
		// The other resolution path: a file that raised an issue is later gone, so discovery reports the path as a
		// non-candidate (ReportInspectIgnored). That clean report must clear the flag for the scope.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectCandidate(Candidate("Objectives/Gone.md", VaultSyncAction.Conflict, "policy violation: unknown file placement"));
		Assert.Single(registry.GetActiveStatuses());

		watcher.ReportInspectIgnored(Vault.AbsolutePath("Objectives/Gone.md"));

		Assert.Empty(registry.GetActiveStatuses());
	}

	[Fact]
	public void A_successful_sync_resolves_every_reason_the_inspection_raised()
	{
		// The core of the purge-lingering fix: a candidate that trips several reason codes at once must have *all* of
		// them resolved on a successful sync, not only SyncFailed.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		var candidate = Candidate(
			"Objectives/Messy.md",
			VaultSyncAction.PurgeFile,
			"puck violation and policy rejection",
			issues: [new MarkdownValidationIssue("id", "bad identity")]);
		watcher.ReportInspectCandidate(candidate);
		Assert.True(registry.GetActiveStatuses().Count >= 2, "expected the candidate to raise multiple reason codes");

		watcher.ReportSyncSucceeded(candidate);

		Assert.Empty(registry.GetActiveStatuses());
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact(Skip = "PEP108 phase D (structured outcomes, with REFACTOR Alpha phase 4): one root cause should yield one classified reason. Today the three string-sniffing heuristics each match independently, so a single file raises MarkdownInvalid + PuckViolation + PolicyViolation.")]
	public void One_bad_file_should_raise_a_single_classified_issue()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		// One file, one root cause — a bad PUCK that also trips the policy reject path and carries a validation issue.
		watcher.ReportInspectCandidate(Candidate(
			"Objectives/Bad.md",
			VaultSyncAction.PurgeFile,
			"Implicit storage rejects unknown frontmatter PUCK assertion.",
			issues: [new MarkdownValidationIssue("id", "puck mismatch")]));

		Assert.Single(registry.GetActiveStatuses());
	}

	[Fact(Skip = "REFACTOR Alpha phase 4 + dismiss feature: a foreign, unmanaged file in a non-exclusive root is not a system error. The mode policy should classify it as a dismissible warning (and leave it in place), not an Error-severity policy violation to be purged.")]
	public void Foreign_file_in_a_non_exclusive_root_should_surface_as_a_warning()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		// A stray, unmanaged note in a shared implicit root — roots are not required to be exclusive.
		watcher.ReportInspectCandidate(Candidate(
			"Objectives/My personal note.md",
			VaultSyncAction.PurgeFile,
			"Implicit storage rejects unknown frontmatter PUCK assertion."));

		Assert.Equal(OperationSeverity.Warning, Assert.Single(registry.GetActiveStatuses()).Severity);
	}

	[Fact]
	public async Task System_api_report_preserves_the_watcher_contract()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md", VaultSyncAction.Conflict, "policy violation: unknown file placement"));

		var report = await Vault.WithScopeAsync(services => services
			.GetRequiredService<ISystemApi>()
			.GetWatcherIssuesAsync(TestContext.Current.CancellationToken));

		Assert.Equal("issues", report.Status);
		Assert.Equal(1, report.IssueCount);
		Assert.Equal(1, report.CriticalIssueCount);
		var record = Assert.Single(report.Issues);
		Assert.Equal(WatcherOperations.Reconcile, record.Type);
		Assert.Equal("policy", record.Category);
		Assert.True(record.IsCritical);
		Assert.Equal(WatcherOperations.PolicyViolation, record.Criterion);
		Assert.Equal("error", record.Severity);
		Assert.Contains(record.Files, file => file.Replace('\\', '/') == "Objectives/Bad.md");
	}
}
