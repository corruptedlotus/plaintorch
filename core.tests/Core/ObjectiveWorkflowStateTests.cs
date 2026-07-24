using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

public sealed class ObjectiveWorkflowStateTests : VaultTestBase
{
	[Fact]
	public async Task Failed_objective_settles_like_other_terminal_states()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Ship it", requestedId: "j10000001", cancellationToken: cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 7), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Failed), cancellationToken));

		var transactions = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == objective.Id)
			.ToListAsync(cancellationToken));

		var transaction = Assert.Single(transactions);
		Assert.True(transaction.Amount > 0);
		Assert.Contains("Failed", transaction.Description);
	}

	[Fact]
	public async Task Failed_objective_does_not_get_promoted_when_added_to_onrush()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Ship it", requestedId: "j10000002", cancellationToken: cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Failed), cancellationToken));

		var sprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var updated = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.AddToOnrushAsync(objective.Id, sprint.Id, cancellationToken));

		Assert.Equal(ObjectiveStatus.Failed, updated.Status);
		Assert.Equal(sprint.Id, updated.OnrushSprintId);
	}

	[Fact]
	public async Task Objective_without_onrush_settles_at_single_multiplier()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Ship it", requestedId: "j10000003", cancellationToken: cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 7), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), cancellationToken));

		var transactions = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == objective.Id)
			.ToListAsync(cancellationToken));

		var transaction = Assert.Single(transactions);
		Assert.Null(objective.OnrushSprintId);
		Assert.Equal(7, transaction.Amount);
	}

	[Fact]
	public async Task Objective_in_active_onrush_settles_at_double_multiplier()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Ship it", requestedId: "j10000004", cancellationToken: cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 7), cancellationToken));

		var sprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.AddToOnrushAsync(objective.Id, sprint.Id, cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), cancellationToken));

		var transactions = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == objective.Id)
			.ToListAsync(cancellationToken));

		var transaction = Assert.Single(transactions);
		Assert.Equal(14, transaction.Amount);
	}
}