using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers what the change feed announces, which is what lets a client learn about writes it did not make.
/// </summary>
public sealed class ChangeFeedTests : VaultTestBase
{
	private async Task<IReadOnlyList<EntityChange>> RecordAsync(Func<Task> action)
	{
		var broker = Vault.GetSingleton<PlaintorchChangeBroker>();
		using var subscription = broker.Subscribe();

		await action();

		var changes = new List<EntityChange>();
		while (subscription.Reader.TryRead(out var change))
		{
			changes.Add(change);
		}

		return changes;
	}

	[Fact]
	public async Task Creating_an_entity_announces_it_under_its_runtime_type()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		Objective? objective = null;
		var changes = await RecordAsync(async () => objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Feed me", cancellationToken: cancellationToken)));

		Assert.Contains(changes, change =>
			change.Type == nameof(Objective)
			&& change.Id == objective!.Id
			&& change.Operation == EntityChangeOperation.Added);
	}

	[Fact]
	public async Task Updating_an_entity_announces_a_modification()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Before", cancellationToken: cancellationToken));

		var changes = await RecordAsync(async () => await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(Title: "After"), cancellationToken)));

		Assert.Contains(changes, change =>
			change.Type == nameof(Objective)
			&& change.Id == objective.Id
			&& change.Operation == EntityChangeOperation.Modified);
	}

	[Fact]
	public async Task A_declarative_is_announced_as_the_kind_it_actually_is()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		Fate? fate = null;
		var changes = await RecordAsync(async () => fate = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("An event"), cancellationToken)));

		// Polymorphic entities share a table but must be announced under the type the API serializes them
		// as, or a client cannot match the announcement to anything it holds.
		Assert.Contains(changes, change => change.Type == nameof(Fate) && change.Id == fate!.Id);
		Assert.DoesNotContain(changes, change => change.Type == nameof(Incentive));
	}

	[Fact]
	public async Task A_child_record_is_announced_as_a_change_to_its_owner()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var cycle = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var changes = await RecordAsync(async () => await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.PlanExecutiveAsync(
				new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, Title: "Do the thing"),
				cycle.Id,
				cancellationToken)));

		// An executive has no identity a client tracks, so the cycle holding it is what gets announced.
		Assert.Contains(changes, change =>
			change.Type == nameof(PolarisCycle)
			&& change.Id == cycle.Id
			&& change.Operation == EntityChangeOperation.Modified);
		Assert.DoesNotContain(changes, change => change.Type == nameof(Executive));
	}

	[Fact]
	public async Task A_preference_write_is_announced_under_its_key()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var gregorian = System.Text.Json.JsonDocument.Parse("\"Gregorian\"");

		var set = await RecordAsync(async () => await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPreferenceApi>()
			.SetAsync(PreferenceKeys.DefaultCalendar, gregorian.RootElement, cancellationToken)));
		var reset = await RecordAsync(async () => await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPreferenceApi>()
			.ResetAsync(PreferenceKeys.DefaultCalendar, cancellationToken)));

		// A preference has no identity a client tracks, so it is announced under its own type: the client matches it
		// to nothing and only revalidates what it observes, which is how a calendar switch reaches an open window.
		Assert.Contains(set, change =>
			change.Type == PlaintorchChangeFeedInterceptor.UserPreferenceChangeType
			&& change.Id == PreferenceKeys.DefaultCalendar
			&& change.Operation == EntityChangeOperation.Added);
		Assert.Contains(reset, change =>
			change.Type == PlaintorchChangeFeedInterceptor.UserPreferenceChangeType
			&& change.Id == PreferenceKeys.DefaultCalendar
			&& change.Operation == EntityChangeOperation.Deleted);
	}

	[Fact]
	public async Task Nothing_is_announced_when_nothing_is_saved()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var changes = await RecordAsync(async () => await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.ListAsync(cancellationToken)));

		Assert.Empty(changes);
	}
}
