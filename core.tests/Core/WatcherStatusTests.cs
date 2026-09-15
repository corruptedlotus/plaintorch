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
		VaultSyncConcern concern = VaultSyncConcern.None,
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
			SuggestedReason: reason,
			Concern: concern);
	}

	[Fact]
	public void Policy_violation_candidate_raises_then_a_clean_inspection_resolves_it()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md", VaultSyncAction.Conflict, "freeform ownership disallowed in a managed root", VaultSyncConcern.PolicyViolation));

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
	public void A_passive_read_failure_surfaces_as_a_retryable_warning_keeping_its_cause()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		watcher.ReportInspectFailure(
			Vault.AbsolutePath("Objectives/Denied.md"),
			new VaultFileAccessException(VaultFileAccessKind.PermissionDenied, "Objectives/Denied.md", new UnauthorizedAccessException()));

		// Tier 1: a passive-read failure is advisory. The underlying cause is preserved in the reason code, but the
		// severity is a warning that does not, on its own, degrade health — the file is simply left and re-checked.
		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.PermissionDenied, status.ReasonCode);
		Assert.Equal(OperationSeverity.Warning, status.Severity);
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		// A later clean inspection of the same path resolves the flag through ordinary diff-based reporting.
		watcher.ReportInspectIgnored(Vault.AbsolutePath("Objectives/Denied.md"));
		Assert.Empty(registry.GetActiveStatuses());
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

		var candidate = Candidate("Objectives/Stray.md", VaultSyncAction.PurgeFile, "Unknown file is disallowed by enforced storage policy.", VaultSyncConcern.PolicyViolation);
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

		watcher.ReportInspectCandidate(Candidate("Objectives/Gone.md", VaultSyncAction.Conflict, "policy violation: unknown file placement", VaultSyncConcern.PolicyViolation));
		Assert.Single(registry.GetActiveStatuses());

		watcher.ReportInspectIgnored(Vault.AbsolutePath("Objectives/Gone.md"));

		Assert.Empty(registry.GetActiveStatuses());
	}

	[Fact]
	public void A_successful_sync_resolves_the_single_reason_the_inspection_raised()
	{
		// The purge-lingering fix, post phase D: one root cause yields one classified reason, so a candidate that trips
		// a puck rejection while also carrying a validation issue raises exactly one status — the puck concern subsumes
		// the issue. The successful-sync report must still pass the *whole* reconcile check-set, because the purge path
		// suppresses re-inspection and the reporter cannot know which concern was active; a success that passed only
		// SyncFailed would strand the raised concern.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		var candidate = Candidate(
			"Objectives/Messy.md",
			VaultSyncAction.PurgeFile,
			"puck violation and policy rejection",
			VaultSyncConcern.PuckViolation,
			issues: [new MarkdownValidationIssue("id", "bad identity")]);
		watcher.ReportInspectCandidate(candidate);
		Assert.Equal(WatcherOperations.PuckViolation, Assert.Single(registry.GetActiveStatuses()).ReasonCode);

		watcher.ReportSyncSucceeded(candidate);

		Assert.Empty(registry.GetActiveStatuses());
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact] // Phase D: fixed — the decision carries one structured concern, so one root cause yields one classified reason.
	public void One_bad_file_should_raise_a_single_classified_issue()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		// One file, one root cause — a bad PUCK that also trips the policy reject path and carries a validation issue.
		// The policy classifies this as a single PuckViolation; the incidental validation issue does not raise a second.
		watcher.ReportInspectCandidate(Candidate(
			"Objectives/Bad.md",
			VaultSyncAction.PurgeFile,
			"Implicit storage rejects unknown frontmatter PUCK assertion.",
			VaultSyncConcern.PuckViolation,
			issues: [new MarkdownValidationIssue("id", "puck mismatch")]));

		Assert.Equal(WatcherOperations.PuckViolation, Assert.Single(registry.GetActiveStatuses()).ReasonCode);
	}

	[Fact]
	public void An_unrecognised_identity_assertion_surfaces_as_an_error()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		// A file asserting a PUCK the vault does not recognise is illegal, not merely foreign: it is still left in place
		// (Ignore, not purged), but it is an error demanding manual resolution — it degrades health rather than sitting
		// as a silent advisory.
		watcher.ReportInspectCandidate(Candidate(
			"Objectives/My personal note.md",
			VaultSyncAction.Ignore,
			"Unrecognised implicit PUCK assertion is left in place as an unmanaged file.",
			VaultSyncConcern.ForeignFile));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.ForeignFile, status.ReasonCode);
		Assert.Equal(OperationSeverity.Error, status.Severity);
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());
	}

	[Fact]
	public void An_unrecognised_assertion_persists_through_its_own_successful_sync()
	{
		// Leaving the file in place IS the (no-op) sync outcome, so the standing error must outlive it — unlike an
		// actionable reason, which a successful sync clears. It resolves only when the user acts on the file.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		var candidate = Candidate(
			"Notes/Stray.md",
			VaultSyncAction.Ignore,
			"Unrecognised freeform PUCK assertion is left in place as an unmanaged file.",
			VaultSyncConcern.ForeignFile);
		watcher.ReportInspectCandidate(candidate);
		Assert.Equal(WatcherOperations.ForeignFile, Assert.Single(registry.GetActiveStatuses()).ReasonCode);

		watcher.ReportSyncSucceeded(candidate);

		Assert.Equal(WatcherOperations.ForeignFile, Assert.Single(registry.GetActiveStatuses()).ReasonCode);
	}

	[Fact]
	public void An_unrecognised_assertion_resolves_when_the_file_is_gone_or_becomes_managed()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		var foreign = Candidate(
			"Notes/Stray.md",
			VaultSyncAction.Ignore,
			"Unrecognised freeform PUCK assertion is left in place as an unmanaged file.",
			VaultSyncConcern.ForeignFile);

		// The file vanishes: discovery reports the path as a non-candidate, which clears the advisory.
		watcher.ReportInspectCandidate(foreign);
		Assert.Single(registry.GetActiveStatuses());
		watcher.ReportInspectIgnored(Vault.AbsolutePath("Notes/Stray.md"));
		Assert.Empty(registry.GetActiveStatuses());

		// The file becomes managed: a clean re-inspection (no concern) clears the advisory.
		watcher.ReportInspectCandidate(foreign);
		Assert.Single(registry.GetActiveStatuses());
		watcher.ReportInspectCandidate(Candidate("Notes/Stray.md"));
		Assert.Empty(registry.GetActiveStatuses());
	}

	[Fact]
	public void Vault_inaccessibility_is_an_error_that_sleeps_and_resolves_on_recovery()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		// Tier 2: a whole-of-vault access failure is an error-level issue (it degrades health to "issues"). It is keyed on
		// the global scope so there is exactly one such status regardless of which root is currently unreachable; the
		// offending root is carried in its files. This is the signal the watcher goes to sleep on.
		var root = Vault.AbsolutePath("Directives");
		watcher.ReportVaultInaccessible(root, "permission denied");

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.VaultInaccessible, status.ReasonCode);
		Assert.Equal(WatcherOperations.VaultAccess, status.OperationId);
		Assert.Equal(WatcherOperations.GlobalScope, status.ScopeKey);
		Assert.Equal(OperationSeverity.Error, status.Severity);
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());
		Assert.Contains(status.Files, file => file.Replace('\\', '/').EndsWith("Directives", StringComparison.Ordinal));

		// A different root failing next re-uses the same status rather than stranding the first (the offending root just
		// moves in the detail/files), so recovery can never leave a ghost tier-2 issue behind.
		watcher.ReportVaultInaccessible(Vault.AbsolutePath("Saga"), "still down");
		Assert.Single(registry.GetActiveStatuses());

		// When the watcher re-probes and access is restored, it reports vault access restored, which resolves the issue.
		watcher.ReportVaultAccessible();
		Assert.Empty(registry.GetActiveStatuses());
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact]
	public async Task System_api_report_preserves_the_watcher_contract()
	{
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md", VaultSyncAction.Conflict, "policy violation: unknown file placement", VaultSyncConcern.PolicyViolation));

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

	[Fact]
	public async Task The_wire_carries_the_reason_message_and_the_occurrence_detail_separately()
	{
		// The status keeps only the specific detail (here, the policy's reason); the generic descriptor message is a
		// function of the reason code and is resolved when the record is built. The client gets both halves apart so it
		// can show, fold, or hide the detail on its own — and the Instance-dismiss fingerprint stays about the problem.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();
		const string reason = "policy violation: unknown file placement";

		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md", VaultSyncAction.Conflict, reason, VaultSyncConcern.PolicyViolation));
		Assert.Equal(reason, Assert.Single(registry.GetActiveStatuses()).Detail);

		var report = await Vault.WithScopeAsync(services => services
			.GetRequiredService<ISystemApi>()
			.GetWatcherIssuesAsync(TestContext.Current.CancellationToken));
		var record = Assert.Single(report.Issues);
		Assert.Equal(WatcherOperations.Describe(WatcherOperations.PolicyViolation).Message, record.Message);
		Assert.Equal(reason, record.Detail);

		// A reason with nothing specific to add carries no detail at all, rather than repeating its message.
		watcher.ReportInspectCandidate(Candidate("Objectives/Bad.md"));
		watcher.ReportStartupScan(succeeded: false);
		var scan = Assert.Single(registry.GetActiveStatuses());
		Assert.Null(scan.Detail);
	}

	[Fact]
	public void The_dismiss_fingerprint_is_structural_not_the_message_text()
	{
		// What identifies "the same problem" for an Instance dismissal is composed from facts — the classified concern,
		// the action, the offending fields and values — so a reworded or translated message leaves it untouched, while a
		// genuinely different problem (another field goes bad) changes it.
		var watcher = Vault.GetSingleton<WatcherStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		VaultSyncCandidate Invalid(string reason, MarkdownValidationIssue issue)
			=> Candidate("Objectives/Bad.md", VaultSyncAction.Conflict, reason, VaultSyncConcern.MarkdownInvalid, issues: [issue]);

		watcher.ReportInspectCandidate(Invalid("Candidate has validation issues.", new("status", "Unknown enum value.", "Nope")));
		var first = Assert.Single(registry.GetActiveStatuses()).Fingerprint;
		Assert.NotNull(first);
		Assert.DoesNotContain("validation", first, StringComparison.OrdinalIgnoreCase);

		// Same facts, different words: the same problem.
		watcher.ReportInspectCandidate(Invalid("Le candidat est invalide.", new("status", "Valeur inconnue.", "Nope")));
		Assert.Equal(first, Assert.Single(registry.GetActiveStatuses()).Fingerprint);

		// Another field goes bad: a different problem.
		watcher.ReportInspectCandidate(Invalid("Candidate has validation issues.", new("forecast", "Field is required.")));
		Assert.NotEqual(first, Assert.Single(registry.GetActiveStatuses()).Fingerprint);
	}
}
