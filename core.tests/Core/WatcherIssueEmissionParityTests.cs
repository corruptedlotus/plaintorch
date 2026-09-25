using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Issue-emission parity: the operation-status issues a startup sweep raises for a planted problem must match the ones
/// a live reconcile of the same problem raises — a sweep must not under- or over-report relative to runtime. This is an
/// A/B integration: the same problem is planted in two isolated vaults, one swept and one reconciled live (both through
/// the real <see cref="VaultWatcherReconciler"/>), and their active issue sets are compared. It guards the gap where the
/// sweep reported only sync outcomes and never the inspection concern, so a file left in place — an unrecognised
/// identity assertion — surfaced as an error at runtime but was silent after a sweep.
/// </summary>
public sealed class WatcherIssueEmissionParityTests
{
	private static string Frontmatter(string puck) => "---" + "\n" + $"puck: {puck}" + "\n" + "---" + "\n";

	/// <summary>
	/// Plants the same problem in two fresh vaults, sweeps one and live-reconciles the planted path in the other (both
	/// emitting), and returns the two normalized active-issue sets. <paramref name="plant"/> returns the planted
	/// problem's vault-relative path (the path the runtime side reconciles).
	/// </summary>
	private static async Task<(IReadOnlyList<string> Sweep, IReadOnlyList<string> Runtime)> RunBothWaysAsync(
		Func<TestVault, Task<string>> plant)
	{
		var sweepVault = new TestVault();
		var runtimeVault = new TestVault();
		await sweepVault.InitializeAsync();
		await runtimeVault.InitializeAsync();
		try
		{
			await plant(sweepVault);
			await sweepVault.SweepWithIssuesAsync();

			var runtimePath = await plant(runtimeVault);
			await runtimeVault.ReconcileWithIssuesAsync(runtimeVault.AbsolutePath(runtimePath));

			return (IssuesOf(sweepVault), IssuesOf(runtimeVault));
		}
		finally
		{
			await runtimeVault.DisposeAsync();
			await sweepVault.DisposeAsync();
		}
	}

	private static IReadOnlyList<string> IssuesOf(TestVault vault)
	{
		return vault.GetSingleton<OperationStatusRegistry>()
			.GetActiveStatuses()
			.Select(status => $"{status.OperationId}|{NormalizeScope(vault, status.ScopeKey)}|{status.ReasonCode}|{status.Severity}")
			.OrderBy(row => row, StringComparer.Ordinal)
			.ToList();
	}

	private static string NormalizeScope(TestVault vault, string scopeKey)
	{
		if (string.Equals(scopeKey, WatcherOperations.GlobalScope, StringComparison.Ordinal) || !Path.IsPathRooted(scopeKey))
		{
			return scopeKey;
		}

		return Path.GetRelativePath(vault.VaultRoot, scopeKey).Replace('\\', '/');
	}

	private static string Issue(string scope, string reasonCode, OperationSeverity severity)
		=> $"{WatcherOperations.Reconcile}|{scope}|{reasonCode}|{severity}";

	[Fact]
	public async Task An_unrecognised_identity_assertion_emits_the_same_error_in_sweep_and_runtime()
	{
		// The case that used to diverge: a note asserting a PUCK the vault does not recognise. Runtime raised a
		// foreign-file error; the sweep reported only the (no-op) sync success and stayed silent.
		var (sweep, runtime) = await RunBothWaysAsync(vault =>
		{
			vault.WriteVaultFile("Objectives/Ghost.md", Frontmatter("j99999999") + "Body." + Environment.NewLine);
			return Task.FromResult("Objectives/Ghost.md");
		});

		Assert.Equal(sweep, runtime);
		Assert.Contains(Issue("Objectives/Ghost.md", WatcherOperations.ForeignFile, OperationSeverity.Error), sweep);
	}

	[Fact]
	public async Task A_clean_entity_emits_no_issues_in_either()
	{
		var (sweep, runtime) = await RunBothWaysAsync(async vault =>
		{
			vault.WriteVaultFile("Objectives/Task.md", "# Task" + Environment.NewLine + "Body." + Environment.NewLine);
			await vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
				.InitializeFromPathAsync("Objectives/Task.md", TestContext.Current.CancellationToken));
			return "Objectives/Task.md";
		});

		Assert.Empty(sweep);
		Assert.Equal(sweep, runtime);
	}

	[Fact]
	public async Task An_enforced_root_stray_emits_matching_issues_in_sweep_and_runtime()
	{
		// Whatever the enforced (Journal/Polaris) root decides for a stray non-entity file, the sweep and runtime must
		// decide — and emit — identically. (A purge raises then resolves the policy concern, netting no standing issue.)
		var (sweep, runtime) = await RunBothWaysAsync(vault =>
		{
			vault.WriteVaultFile("Journal/Just A Stray.md", "# Stray" + Environment.NewLine + "Not a cycle." + Environment.NewLine);
			return Task.FromResult("Journal/Just A Stray.md");
		});

		Assert.Equal(sweep, runtime);
	}

	[Fact]
	public async Task A_markdown_invalid_edit_to_a_known_entity_emits_matching_issues()
	{
		// An existing objective whose frontmatter is then corrupted. Both paths raise the same markdown concern (and both
		// resolve it the same way via rewrite-from-database) — parity regardless of the net outcome.
		var (sweep, runtime) = await RunBothWaysAsync(async vault =>
		{
			vault.WriteVaultFile("Objectives/Real.md", "# Real" + Environment.NewLine + "Body." + Environment.NewLine);
			await vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
				.InitializeFromPathAsync("Objectives/Real.md", TestContext.Current.CancellationToken));
			// Corrupt a mapped field so the candidate carries a validation issue.
			await File.AppendAllTextAsync(vault.AbsolutePath("Objectives/Real.md"), "status: not-a-real-status" + Environment.NewLine, TestContext.Current.CancellationToken);
			return "Objectives/Real.md";
		});

		Assert.Equal(sweep, runtime);
	}

	[Fact]
	public async Task A_blocked_delete_emits_the_same_standing_error_in_sweep_and_runtime()
	{
		// A begun objective's note is deleted while executive records still reference it: the sweep reaches it through the
		// orphan pass, runtime through the delete event, and both must raise the one delete-blocked error — no sync-failed.
		var (sweep, runtime) = await RunBothWaysAsync(async vault =>
		{
			var objective = await vault.SeedStandaloneObjectiveAsync("Held");
			await vault.BeginObjectiveBoundaryAsync(objective.Id);
			await vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().PlanExecutiveAsync(
				new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id),
				cancellationToken: TestContext.Current.CancellationToken));
			File.Delete(vault.AbsolutePath("Objectives/Held.md"));
			return "Objectives/Held.md";
		});

		Assert.Equal(sweep, runtime);
		Assert.Equal([Issue("Objectives/Held.md", WatcherOperations.DeleteBlocked, OperationSeverity.Error)], sweep);
	}
}
