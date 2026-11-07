using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// One amount rule for every reward (PEP103 I-043, user ruling): taking a reward back removes the grant transaction that
/// exists, whatever the work's reward amount is now (0 included), and only granting reads the amount. A grant of 0 books no
/// row, settlement included.
/// </summary>
public sealed class ZeroAmountRewardTests : VaultTestBase
{
	private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));

	private Task<List<CelestronTransaction>> LedgerOfAsync(string sourceId)
		=> Vault.QueryAsync(context => context.CelestronLedger.AsNoTracking().Where(item => item.SourcePuck == sourceId).ToListAsync(Ct));

	private Task<Decree> LowerRewardToZeroAsync(string decreeId)
		=> Declarative(api => api.UpdateDecreeAsync(decreeId, new DecreeUpdate(ActiveCelestron: 0), Ct));

	[Fact]
	public async Task A_reflectives_grant_is_revoked_after_its_decree_reward_drops_to_zero()
	{
		var lunar = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Evening reflection", DirectiveId: lunar.Id, Orbit: "d", ActiveCelestron: 5, Reflect: true), Ct));
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var reflective = await Vault.QueryAsync(context => context.Set<Reflective>().AsNoTracking()
			.SingleAsync(item => item.PolarisCycleId == cycle.Id && item.DecreeId == decree.Id, Ct));

		await Polaris(api => api.UpdateReflectiveAsync(reflective.Id, new ReflectiveUpdate(Executed: true), Ct));
		Assert.Equal(5, Assert.Single(await LedgerOfAsync(decree.Id)).Amount);

		await LowerRewardToZeroAsync(decree.Id);
		await Polaris(api => api.UpdateReflectiveAsync(reflective.Id, new ReflectiveUpdate(Executed: false), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));

		// Executed again at 0, it is granted nothing.
		await Polaris(api => api.UpdateReflectiveAsync(reflective.Id, new ReflectiveUpdate(Executed: true), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));
	}

	[Fact]
	public async Task An_executives_grant_is_revoked_after_its_decree_reward_drops_to_zero()
	{
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Routine", ActiveCelestron: 5), Ct));
		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id), cycle.Id, Ct));

		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: true), Ct));
		Assert.Equal(5, Assert.Single(await LedgerOfAsync(decree.Id)).Amount);

		await LowerRewardToZeroAsync(decree.Id);
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: false), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));

		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: true), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));
	}

	[Fact]
	public async Task An_executive_executed_at_zero_earns_nothing_for_a_move()
	{
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var tomorrow = await Polaris(api => api.PlanAsync(Today, 1, cancellationToken: Ct));
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Routine"), Ct));
		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id), cycle.Id, Ct));
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: true), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));

		// A move is no grant: the reward raised since is not read until the executive is executed again.
		await Declarative(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(ActiveCelestron: 5), Ct));
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(MoveToPolarisCycleId: tomorrow.Id), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));
	}

	[Fact]
	public async Task An_attentives_grant_is_revoked_after_its_decree_reward_drops_to_zero()
	{
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Stretch", ActiveCelestron: 3), Ct));
		var occurrence = new AttentiveOccurrenceRef(decree.Id, new DateTime(2026, 8, 1));

		await Declarative(api => api.UpdateAttentiveAsync(occurrence, new AttentiveUpdate(Resolution: AttentiveResolution.Done), Ct));
		Assert.Equal(3, Assert.Single(await LedgerOfAsync(decree.Id)).Amount);

		await LowerRewardToZeroAsync(decree.Id);
		await Declarative(api => api.UpdateAttentiveAsync(occurrence, new AttentiveUpdate(Resolution: AttentiveResolution.Skipped), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));

		await Declarative(api => api.UpdateAttentiveAsync(occurrence, new AttentiveUpdate(Resolution: AttentiveResolution.Done), Ct));
		Assert.Empty(await LedgerOfAsync(decree.Id));
	}

	[Fact]
	public async Task A_settlement_of_zero_books_no_row_and_a_finished_move_settles_nothing_anew()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Free work", requestedId: "j10000101", cancellationToken: Ct));
		Task ShiftAsync(ObjectiveStatus status) => Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(status), Ct));

		await ShiftAsync(ObjectiveStatus.Done);
		Assert.Empty(await LedgerOfAsync(objective.Id));

		// Done to Archived is no finishing, so the value raised meanwhile is not read.
		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 7), Ct));
		await ShiftAsync(ObjectiveStatus.Archived);
		Assert.Empty(await LedgerOfAsync(objective.Id));

		// Finishing anew reads it.
		await ShiftAsync(ObjectiveStatus.Failed);
		await ShiftAsync(ObjectiveStatus.Done);
		Assert.Equal(7, Assert.Single(await LedgerOfAsync(objective.Id)).Amount);
	}

	[Fact]
	public async Task A_settlement_is_revoked_after_the_objectives_value_drops_to_zero()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Paid work", requestedId: "j10000102", cancellationToken: Ct));
		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 7), Ct));
		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		Assert.Equal(7, Assert.Single(await LedgerOfAsync(objective.Id)).Amount);

		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 0), Ct));
		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Failed), Ct));
		Assert.Empty(await LedgerOfAsync(objective.Id));
	}
}
