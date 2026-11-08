using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// PUCK issuance runs on a bare context of its own: minting an id registers it, and saves nothing the caller's context has
/// pending. It used to save on the caller's context, synchronously, which stored the caller's pending changes past every
/// save hook (the state policy, backlog records, the change feed) — a sprint being planned was inserted that way when its
/// milestone's id was minted.
/// </summary>
public sealed class PuckIssuanceIsolationTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Minting_an_id_saves_nothing_the_caller_has_pending()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Before");

		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			var tracked = await context.Objectives.SingleAsync(item => item.Id == objective.Id, Ct);
			tracked.Title = "After";

			var minted = services.GetRequiredService<PuckCreationService>().CreateIdFor<Checkpoint>();

			// The id is registered at once; the caller's change is still only the caller's.
			Assert.True(await Vault.QueryAsync(other => other.PuckRegistryEntries.AnyAsync(entry => entry.Id == minted, Ct)));
			Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
			Assert.Equal("Before", await Vault.QueryAsync(other => other.Objectives.AsNoTracking().Where(item => item.Id == objective.Id).Select(item => item.Title).SingleAsync(Ct)));

			await context.SaveChangesAsync(Ct);
		});

		Assert.Equal("After", await Vault.QueryAsync(context => context.Objectives.AsNoTracking().Where(item => item.Id == objective.Id).Select(item => item.Title).SingleAsync(Ct)));
	}

	[Fact]
	public async Task A_minted_id_stays_issued_when_the_caller_never_saves()
	{
		// The registry commits on its own, so an id whose entity is never saved is spent, like a sequence value: the next
		// mint does not hand it out again.
		var first = await Vault.WithScopeAsync(services => Task.FromResult(services.GetRequiredService<PuckCreationService>().CreateIdFor<OnrushSprint>()));
		var second = await Vault.WithScopeAsync(services => Task.FromResult(services.GetRequiredService<PuckCreationService>().CreateIdFor<OnrushSprint>()));

		Assert.NotEqual(first, second);
		Assert.Equal(2, await Vault.QueryAsync(context => context.PuckRegistryEntries.CountAsync(entry => entry.Id == first || entry.Id == second, Ct)));
		Assert.Empty(await Vault.QueryAsync(context => context.OnrushSprints.AsNoTracking().Where(item => item.Id == first || item.Id == second).ToListAsync(Ct)));
	}

	[Fact]
	public async Task Planning_a_sprint_goes_through_the_onrush_rules()
	{
		// The planning sprint used to be inserted by the milestone id's mint, before the onrush rules could see it, so a
		// second plan failed on the raw unique key. Saved by the caller now, it meets the rule that refuses it.
		var planned = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().PlanAsync(new OnrushSprintPlan("First plan"), Ct));
		Assert.False(string.IsNullOrWhiteSpace(planned.MilestoneCheckpointId));

		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() => Vault.WithScopeAsync(services =>
			services.GetRequiredService<IOnrushSprintApi>().PlanAsync(new OnrushSprintPlan("Second plan"), Ct)));
		Assert.Contains("planning onrush sprint already exists", refused.Message, StringComparison.OrdinalIgnoreCase);
		// A structure the model cannot hold is systemic: refused at every enforcement level (PEP103).
		Assert.Equal(StateConflictKind.Systemic, Assert.Single(refused.Decision.Conflicts).Kind);

		// The first plan and its milestone are untouched.
		var stored = await Vault.QueryAsync(context => context.OnrushSprints.AsNoTracking().SingleAsync(item => item.Id == OnrushSprint.PlanningPlaceholderId, Ct));
		Assert.Equal("First plan", stored.Title);
		Assert.Equal(planned.MilestoneCheckpointId, stored.MilestoneCheckpointId);
	}
}
