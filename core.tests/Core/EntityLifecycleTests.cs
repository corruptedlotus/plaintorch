using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The policy-derived entity lifecycle actions (REFACTOR Alpha phase 5a): the storage-mode-mechanical
/// <c>begin</c>/<c>init</c> actions are served by one generic implementation dispatched through the phase-4 mode
/// policy objects, replacing the per-entity copies. The per-type <c>Begin*BoundaryAsync</c> methods delegate here, so
/// their existing tests are the begin-parity gate; these lock the generic action's own guards.
/// </summary>
public sealed class EntityLifecycleTests : VaultTestBase
{
	[Fact]
	public async Task Begin_boundary_is_rejected_for_a_mode_that_does_not_gate_on_a_boundary()
	{
		// Boundary-begin is only meaningful where the storage mode withholds the file until a boundary is begun
		// (Implicit). A Synced entity (LorePage) materializes on create, so it has no boundary to begin — the generic
		// action refuses it through the mode policy rather than a type check.
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<VaultEntityLifecycleService>()
			.BeginBoundaryAsync(typeof(LorePage), "l00000001", TestContext.Current.CancellationToken)));

		Assert.Contains("synchronization boundary", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Init_from_a_puckless_file_creates_an_implicit_incentive()
	{
		// New in phase 5a: init generalises beyond directives to implicit incentives. A user-authored objective file
		// with no frontmatter PUCK is adopted into a new objective, minting an identity through the same generic
		// manual-init path that directive init uses.
		Vault.WriteVaultFile("Objectives/Ship it.md", "# Ship it" + Environment.NewLine + "Authored body." + Environment.NewLine);

		var created = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.InitializeFromPathAsync("Objectives/Ship it.md", TestContext.Current.CancellationToken));

		Assert.False(string.IsNullOrWhiteSpace(created.Id));
		Assert.Equal("Ship it", created.Title);
		Assert.True(await Vault.QueryAsync(context => context.Objectives
			.AnyAsync(item => item.Id == created.Id, TestContext.Current.CancellationToken)));
		Assert.Contains($"puck: {created.Id}", Vault.ReadVaultFile("Objectives/Ship it.md"));
	}

	[Fact]
	public async Task Init_records_a_kind_derived_audit_action()
	{
		// The audit action derives from the entity kind, so directive and objective init are distinguishable in the log
		// without per-entity strings.
		Vault.WriteVaultFile("Objectives/Ship it.md", "# Ship it" + Environment.NewLine);
		var created = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.InitializeFromPathAsync("Objectives/Ship it.md", TestContext.Current.CancellationToken));

		var audited = await Vault.QueryAsync(context => context.AuditLogEntries
			.AnyAsync(entry => entry.Action == "objective.init" && entry.SubjectId == created.Id, TestContext.Current.CancellationToken));
		Assert.True(audited);
	}
}
