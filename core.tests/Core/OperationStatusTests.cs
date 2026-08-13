using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Plaintorch.Diagnostics;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The operation-status registry (PEP108) derives raise/escalate/de-escalate/resolve transitions by diffing a
/// report against current state, deduplicated by (operation, scope, reason), and rolls up health across a single
/// graded severity scale that distinguishes suspension.
/// </summary>
public sealed class OperationStatusRegistryTests
{
	private const string Operation = "watcher.sync";
	private const string Scope = "Objectives/Ship it.md";

	private static OperationReport Report(params OperationCheck[] checks) => new(Operation, Scope, checks);

	[Fact]
	public void First_failure_raises_a_status()
	{
		var registry = new OperationStatusRegistry();

		var transitions = registry.Ingest(Report(OperationCheck.Fail("permission-denied", OperationSeverity.Error, "denied")));

		var transition = Assert.Single(transitions);
		Assert.Equal(OperationStatusTransitionKind.Raised, transition.Kind);
		Assert.Equal("permission-denied", transition.ReasonCode);
		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(OperationSeverity.Error, status.Severity);
		Assert.Equal(1, status.OccurrenceCount);
	}

	[Fact]
	public void Same_severity_repeat_bumps_occurrence_without_a_transition()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Report(OperationCheck.Fail("permission-denied", OperationSeverity.Error)));

		var repeat = registry.Ingest(Report(OperationCheck.Fail("permission-denied", OperationSeverity.Error)));

		Assert.Empty(repeat);
		Assert.Equal(2, Assert.Single(registry.GetActiveStatuses()).OccurrenceCount);
	}

	[Fact]
	public void Rising_severity_escalates()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Report(OperationCheck.Fail("file-in-use", OperationSeverity.Warning)));

		var transitions = registry.Ingest(Report(OperationCheck.Fail("file-in-use", OperationSeverity.Error)));

		var transition = Assert.Single(transitions);
		Assert.Equal(OperationStatusTransitionKind.Escalated, transition.Kind);
		Assert.Equal(OperationSeverity.Warning, transition.PreviousSeverity);
		Assert.Equal(OperationSeverity.Error, transition.Severity);
	}

	[Fact]
	public void Falling_severity_deescalates()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Report(OperationCheck.Fail("file-in-use", OperationSeverity.Error)));

		var transitions = registry.Ingest(Report(OperationCheck.Fail("file-in-use", OperationSeverity.Suspended)));

		Assert.Equal(OperationStatusTransitionKind.Deescalated, Assert.Single(transitions).Kind);
	}

	[Fact]
	public void Passing_check_resolves_and_records_recent_resolution()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Report(OperationCheck.Fail("permission-denied", OperationSeverity.Error)));

		var transitions = registry.Ingest(Report(OperationCheck.Pass("permission-denied")));

		Assert.Equal(OperationStatusTransitionKind.Resolved, Assert.Single(transitions).Kind);
		Assert.Empty(registry.GetActiveStatuses());
		Assert.Equal("permission-denied", Assert.Single(registry.GetRecentResolved()).ReasonCode);
	}

	[Fact]
	public void Distinct_reasons_on_one_scope_are_distinct_statuses()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Report(
			OperationCheck.Fail("permission-denied", OperationSeverity.Error),
			OperationCheck.Fail("file-in-use", OperationSeverity.Suspended)));

		Assert.Equal(2, registry.GetActiveStatuses().Count);

		// Resolve only one reason; the other remains flagged.
		var transitions = registry.Ingest(Report(
			OperationCheck.Pass("permission-denied"),
			OperationCheck.Fail("file-in-use", OperationSeverity.Suspended)));

		var resolved = Assert.Single(transitions);
		Assert.Equal(OperationStatusTransitionKind.Resolved, resolved.Kind);
		Assert.Equal("permission-denied", resolved.ReasonCode);
		Assert.Equal("file-in-use", Assert.Single(registry.GetActiveStatuses()).ReasonCode);
	}

	[Fact]
	public void Health_rolls_up_across_the_graded_scale_and_honours_overrides()
	{
		var registry = new OperationStatusRegistry();
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		// A warning alone does not degrade health.
		registry.Ingest(new OperationReport(Operation, "a.md", [OperationCheck.Fail("advisory", OperationSeverity.Warning)]));
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		// A suspension does.
		registry.Ingest(new OperationReport(Operation, "b.md", [OperationCheck.Fail("file-in-use", OperationSeverity.Suspended)]));
		Assert.Equal(OperationHealth.Suspended, registry.GetHealth());

		// An error outranks suspension.
		registry.Ingest(new OperationReport(Operation, "c.md", [OperationCheck.Fail("permission-denied", OperationSeverity.Error)]));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());

		// A lifecycle override wins, and clearing it restores the derived rollup.
		registry.SetHealthOverride(OperationHealth.Offline);
		Assert.Equal(OperationHealth.Offline, registry.GetHealth());
		registry.SetHealthOverride(null);
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());
	}

	[Fact]
	public void Status_carries_involved_files_and_entity()
	{
		var registry = new OperationStatusRegistry();

		registry.Ingest(Report(OperationCheck.Fail(
			"relocation-conflict",
			OperationSeverity.Error,
			detail: "moved",
			files: ["old/Ship it.md", "new/Ship it.md"],
			entityId: "j12345678")));

		var status = Assert.Single(registry.GetActiveStatuses());
		Assert.Equal(new[] { "old/Ship it.md", "new/Ship it.md" }, status.Files);
		Assert.Equal("j12345678", status.EntityId);
	}
}

/// <summary>
/// Operation-status transitions (PEP108) are persisted durably through the real DI graph: the reporter forwards
/// to the buffered sink, which drains to the vault database as an append-only raised→resolved log.
/// </summary>
public sealed class OperationStatusDurabilityTests : VaultTestBase
{
	[Fact]
	public async Task Reported_transitions_persist_as_durable_events()
	{
		var reporter = Vault.GetSingleton<OperationStatusReporter>();
		var buffer = Vault.GetSingleton<OperationStatusEventBuffer>();

		reporter.Report(new OperationReport("watcher.sync", "Objectives/Ship it.md",
			[OperationCheck.Fail("permission-denied", OperationSeverity.Error, "denied")]));
		reporter.Report(new OperationReport("watcher.sync", "Objectives/Ship it.md",
			[OperationCheck.Pass("permission-denied")]));

		var persisted = await Vault.WithScopeAsync(services =>
			buffer.DrainAsync(services.GetRequiredService<PlainfraContext>(), TestContext.Current.CancellationToken));
		Assert.Equal(2, persisted);

		var events = await Vault.QueryAsync(context => context.OperationStatusEvents
			.OrderBy(item => item.Id)
			.ToListAsync(TestContext.Current.CancellationToken));

		Assert.Equal(2, events.Count);
		Assert.Equal(OperationStatusTransitionKind.Raised, events[0].Transition);
		Assert.Equal("permission-denied", events[0].ReasonCode);
		Assert.Equal(OperationSeverity.Error, events[0].Severity);
		Assert.Equal("watcher.sync", events[0].OperationId);
		Assert.Equal(OperationStatusTransitionKind.Resolved, events[1].Transition);
	}
}
