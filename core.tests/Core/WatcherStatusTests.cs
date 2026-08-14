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

		watcher.ReportSyncFailure(
			Candidate("Objectives/Locked.md"),
			new IOException("The process cannot access the file because it is being used by another process."));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(WatcherOperations.FileInUse, status.ReasonCode);
		Assert.Equal(OperationSeverity.Suspended, status.Severity);
		Assert.Equal(OperationHealth.Suspended, registry.GetHealth());
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
	}
}
