using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Plaintorch.Diagnostics;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The status-dismissal feature (PEP108): a user can snooze an issue so it stops counting toward health and nagging.
/// Matching is by value, so an <see cref="OperationStatusDismissalScope.Instance"/> snooze self-expires when a
/// <em>different</em> problem arises on the same key (its detail fingerprint changes), never permanently blinding the
/// user to a new problem, and it re-applies to a status re-raised after a restart. File and Reason scopes are the
/// reserved broader-snooze framework.
/// </summary>
public sealed class OperationStatusDismissalRegistryTests
{
	private const string Operation = "watcher.reconcile";
	private const string Scope = "Objectives/Ship it.md";
	private const string Reason = "policy-violation";

	private static OperationReport Fail(string detail, string scope = Scope, string reason = Reason)
		=> new(Operation, scope, [OperationCheck.Fail(reason, OperationSeverity.Error, detail)]);

	private static bool DismissedFlag(OperationStatusRegistry registry, string scope = Scope, string reason = Reason)
		=> registry.GetActiveStatusesWithDismissal()
			.Single(item => item.Status.ScopeKey == scope && item.Status.ReasonCode == reason)
			.Dismissed;

	[Fact]
	public void A_dismissed_status_is_excluded_from_health_and_flagged()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Fail("Unknown file."));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());

		var dismissal = registry.Dismiss(OperationStatusDismissalScope.Instance, Operation, Scope, Reason);

		Assert.NotNull(dismissal);
		Assert.True(DismissedFlag(registry));
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact]
	public void Dismissing_an_inactive_instance_records_nothing()
	{
		var registry = new OperationStatusRegistry();

		// Nothing active on this key, so there is no problem to dismiss and no fingerprint to capture.
		Assert.Null(registry.Dismiss(OperationStatusDismissalScope.Instance, Operation, Scope, Reason));
	}

	[Fact]
	public void The_snooze_holds_while_the_same_problem_persists_but_lifts_when_a_different_one_arises()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Fail("Unknown file."));
		registry.Dismiss(OperationStatusDismissalScope.Instance, Operation, Scope, Reason);

		// Same problem re-observed (same detail): stays snoozed.
		registry.Ingest(Fail("Unknown file."));
		Assert.True(DismissedFlag(registry));
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		// A materially different problem on the same file+reason (the detail changes): the snooze lifts.
		registry.Ingest(Fail("Now it also fails frontmatter validation."));
		Assert.False(DismissedFlag(registry));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());
	}

	[Fact]
	public void The_snooze_reapplies_by_value_to_a_status_re_raised_after_a_restart()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Fail("Unknown file."));
		registry.Dismiss(OperationStatusDismissalScope.Instance, Operation, Scope, Reason);

		// The status resolves and later re-raises identically (as a fresh process's startup scan would): the value
		// match re-applies the dismissal even though the re-raised status is a new instance.
		registry.Ingest(new OperationReport(Operation, Scope, [OperationCheck.Pass(Reason)]));
		Assert.Empty(registry.GetActiveStatuses());
		registry.Ingest(Fail("Unknown file."));

		Assert.True(DismissedFlag(registry));
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact]
	public void Restoring_a_dismissal_makes_the_status_count_again()
	{
		var registry = new OperationStatusRegistry();
		registry.Ingest(Fail("Unknown file."));
		registry.Dismiss(OperationStatusDismissalScope.Instance, Operation, Scope, Reason);
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		var restored = registry.Restore(OperationStatusDismissalScope.Instance, Operation, Scope, Reason);

		Assert.NotNull(restored);
		Assert.False(DismissedFlag(registry));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());
	}

	[Fact]
	public void A_file_scope_dismissal_snoozes_every_reason_on_that_path()
	{
		// Framework check: the reserved File scope matches all reasons on a path, so a future "always ignore this file"
		// covers a whole file regardless of which issues it raises.
		var registry = new OperationStatusRegistry();
		registry.Ingest(new OperationReport(Operation, Scope,
		[
			OperationCheck.Fail("policy-violation", OperationSeverity.Error, "Unknown file."),
			OperationCheck.Fail("markdown-invalid", OperationSeverity.Error, "Bad frontmatter."),
		]));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());

		registry.Dismiss(OperationStatusDismissalScope.File, Operation, Scope, reasonCode: string.Empty);

		Assert.All(registry.GetActiveStatusesWithDismissal(), item => Assert.True(item.Dismissed));
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}

	[Fact]
	public void A_reason_scope_dismissal_snoozes_that_reason_on_every_path()
	{
		// Framework check: the reserved Reason scope matches a reason code across files, for a future "always ignore
		// this issue".
		var registry = new OperationStatusRegistry();
		registry.Ingest(Fail("Unknown file.", scope: "A.md"));
		registry.Ingest(Fail("Unknown file.", scope: "B.md"));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());

		registry.Dismiss(OperationStatusDismissalScope.Reason, Operation, scopeKey: string.Empty, Reason);

		Assert.All(registry.GetActiveStatusesWithDismissal(), item => Assert.True(item.Dismissed));
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());
	}
}

/// <summary>
/// Status dismissals (PEP108) persist through the real DI graph: a dismissal writes a durable row and, loaded back
/// into the registry as a vault session would on activation, re-applies by value to the re-raised status.
/// </summary>
public sealed class OperationStatusDismissalDurabilityTests : VaultTestBase
{
	private const string Operation = "watcher.reconcile";
	private const string Scope = "Objectives/Stray.md";
	private const string Reason = "policy-violation";
	private const string Detail = "Unknown file is disallowed by enforced storage policy.";

	[Fact]
	public async Task A_dismissal_persists_and_reapplies_across_a_reload()
	{
		var reporter = Vault.GetSingleton<OperationStatusReporter>();
		var registry = Vault.GetSingleton<OperationStatusRegistry>();

		reporter.Report(new OperationReport(Operation, Scope, [OperationCheck.Fail(Reason, OperationSeverity.Error, Detail)]));
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());

		var dismissed = await Vault.WithScopeAsync(services => services
			.GetRequiredService<OperationStatusDismissalService>()
			.DismissAsync(OperationStatusDismissalScope.Instance, Operation, Scope, Reason, TestContext.Current.CancellationToken));
		Assert.True(dismissed);
		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		var rows = await Vault.QueryAsync(context => context.OperationStatusDismissals
			.ToListAsync(TestContext.Current.CancellationToken));
		Assert.Equal(Detail, Assert.Single(rows).Fingerprint);

		// Simulate a restart: drop the in-memory set and resolve the live status, so nothing is snoozed and nothing
		// is active — the state a fresh process starts in.
		registry.ClearDismissals();
		reporter.Report(new OperationReport(Operation, Scope, [OperationCheck.Pass(Reason)]));
		Assert.Empty(registry.GetActiveStatuses());

		// The session activation loads durable dismissals before the startup scan re-raises the status.
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<OperationStatusDismissalService>()
			.LoadIntoRegistryAsync(TestContext.Current.CancellationToken));
		reporter.Report(new OperationReport(Operation, Scope, [OperationCheck.Fail(Reason, OperationSeverity.Error, Detail)]));

		Assert.Equal(OperationHealth.Ok, registry.GetHealth());

		// Restoring clears the durable row too.
		var restored = await Vault.WithScopeAsync(services => services
			.GetRequiredService<OperationStatusDismissalService>()
			.RestoreAsync(OperationStatusDismissalScope.Instance, Operation, Scope, Reason, TestContext.Current.CancellationToken));
		Assert.True(restored);
		Assert.Equal(OperationHealth.Issues, registry.GetHealth());
		Assert.Empty(await Vault.QueryAsync(context => context.OperationStatusDismissals
			.ToListAsync(TestContext.Current.CancellationToken)));
	}
}
