using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

public sealed class ExecutiveOrderTests : VaultTestBase
{
	[Fact]
	public async Task Issued_order_composes_puck_from_trimmed_onrush_numeral()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var sprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var order = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync(sprint.Id, new ExecutiveOrderPlan("No Distractions"), cancellationToken));

		// Onrush x0100 composes its first order as x100-o01 (PEP099 trims the numeral's padding).
		Assert.Equal("x100-o01", order.Id);
		Assert.Equal(sprint.Id, order.OnrushSprintId);

		var files = Vault.MarkdownFilesUnder(Vault.Layout.OnrushRoot);
		Assert.Contains(files, file => file.Contains("ExecutiveOrders") && file.Contains("x100-o01 - No Distractions"));
	}

	[Fact]
	public async Task Order_counters_increment_per_owning_onrush()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var firstSprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.EndAsync(firstSprint.Id, cancellationToken: cancellationToken));
		var secondSprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var firstOrder = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync(firstSprint.Id, new ExecutiveOrderPlan("First"), cancellationToken));
		var secondOrder = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync(firstSprint.Id, new ExecutiveOrderPlan("Second"), cancellationToken));
		var otherSprintOrder = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync(secondSprint.Id, new ExecutiveOrderPlan("Other"), cancellationToken));

		Assert.EndsWith("-o01", firstOrder.Id);
		Assert.EndsWith("-o02", secondOrder.Id);
		Assert.EndsWith("-o01", otherSprintOrder.Id);
	}

	[Fact]
	public async Task Order_update_and_delete_round_trip()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var sprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));
		var order = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync(sprint.Id, new ExecutiveOrderPlan("Focus"), cancellationToken));

		var updated = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.UpdateExecutiveOrderAsync(order.Id, new ExecutiveOrderUpdate(
				Summary: "Single-task through the sprint",
				EffectiveFrom: new DateOnly(2026, 7, 14),
				EffectiveUntil: new DateOnly(2026, 7, 21)), cancellationToken));

		Assert.Equal("Single-task through the sprint", updated.Summary);
		Assert.Equal(new DateOnly(2026, 7, 14), updated.EffectiveFrom);

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.DeleteExecutiveOrderAsync(order.Id, cancellationToken));

		var remaining = await Vault.QueryAsync(context => context.ExecutiveOrders.CountAsync(cancellationToken));
		Assert.Equal(0, remaining);
		var files = Vault.MarkdownFilesUnder(Vault.Layout.OnrushRoot);
		Assert.DoesNotContain(files, file => file.Contains("Focus"));
	}

	[Fact]
	public async Task Inverted_effective_window_is_rejected()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var sprint = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		await Assert.ThrowsAsync<ArgumentException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync(sprint.Id, new ExecutiveOrderPlan(
				"Backwards",
				EffectiveFrom: new DateOnly(2026, 7, 21),
				EffectiveUntil: new DateOnly(2026, 7, 14)), cancellationToken)));
	}

	[Fact]
	public async Task Orders_cannot_be_issued_against_the_planning_placeholder_sprint()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.PlanAsync(new OnrushSprintPlan("Planned Sprint"), cancellationToken));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IOnrushSprintApi>()
			.IssueExecutiveOrderAsync("0", new ExecutiveOrderPlan("Too Early"), cancellationToken)));
	}
}
