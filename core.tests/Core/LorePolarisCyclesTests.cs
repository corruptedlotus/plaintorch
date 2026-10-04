using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A lore page's Polaris cycles are the cycles of its active period: from its beginning until its next sibling begins,
/// never past the end of an ancestor's period, and through today while nothing has ended it. A page with no beginning
/// has no period and lists nothing.
/// </summary>
public sealed class LorePolarisCyclesTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<LorePage> CreateLoreAsync(string? parentPuck, string title, int? beginsInDays)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ILorePageApi>()
			.CreateAsync(new LorePageCreateRequest(parentPuck, title, beginsInDays is { } days ? Today.AddDays(days) : null), Ct));

	/// <summary>Works a day in the past: a cycle begun and ended on it.</summary>
	private async Task<PolarisCycle> WorkDayAsync(int daysAgo)
	{
		var start = new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(9, 0)));
		var cycle = await Polaris(api => api.StartNewAsync(start, cancellationToken: Ct));
		await Polaris(api => api.EndAsync(cycle.Id, start.AddHours(8), Ct));
		Assert.Equal(Today.AddDays(-daysAgo), cycle.Day);
		return cycle;
	}

	private async Task<IReadOnlyList<DateOnly>> DaysOfAsync(string lorePuck)
	{
		var listing = await Polaris(api => api.ListForLoreAsync(lorePuck, Ct));
		Assert.NotNull(listing);
		return listing.Cycles.Select(cycle => cycle.Day!.Value).ToList();
	}

	private static DateOnly[] DaysAgo(params int[] days) => days.Select(day => Today.AddDays(-day)).ToArray();

	[Fact]
	public async Task A_page_lists_the_cycles_from_its_beginning_until_its_next_sibling_begins_capped_by_its_ancestors()
	{
		var firstAge = await CreateLoreAsync(null, "First Age", -40);
		var secondAge = await CreateLoreAsync(null, "Second Age", -10);
		var gathering = await CreateLoreAsync(firstAge.Puck, "The Gathering", -40);
		var crossing = await CreateLoreAsync(firstAge.Puck, "The Crossing", -25);

		foreach (var daysAgo in new[] { 38, 30, 25, 20, 11, 10, 3 })
		{
			await WorkDayAsync(daysAgo);
		}

		var today = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var forecast = await Polaris(api => api.PlanAsync(Today, 2, cancellationToken: Ct));

		// An Era closed by the next Era.
		var first = await Polaris(api => api.ListForLoreAsync(firstAge.Puck!, Ct));
		Assert.NotNull(first);
		Assert.Equal(Today.AddDays(-40), first.Beginning);
		Assert.Equal(Today.AddDays(-10), first.EndingExclusive);
		Assert.Equal(Today.AddDays(-11), first.Through);
		Assert.Equal(DaysAgo(38, 30, 25, 20, 11), first.Cycles.Select(cycle => cycle.Day!.Value));

		// A chapter closed by its next sibling.
		Assert.Equal(DaysAgo(38, 30), await DaysOfAsync(gathering.Puck!));

		// The last chapter of a concluded Era has no sibling after it, yet it ends with its Era rather than running on.
		Assert.Equal(DaysAgo(25, 20, 11), await DaysOfAsync(crossing.Puck!));

		// The latest Era is still running: it reaches today, and a forecast past today is not part of it yet.
		var second = await Polaris(api => api.ListForLoreAsync(secondAge.Puck!, Ct));
		Assert.NotNull(second);
		Assert.Null(second.EndingExclusive);
		Assert.Equal(Today, second.Through);
		Assert.Equal(DaysAgo(10, 3).Append(Today), second.Cycles.Select(cycle => cycle.Day!.Value));
		Assert.Contains(second.Cycles, cycle => cycle.Id == today.Id);
		Assert.DoesNotContain(second.Cycles, cycle => cycle.Id == forecast.Id);

		// The cycles come with their plan, as the day listing serves them.
		Assert.All(second.Cycles, cycle => Assert.NotNull(cycle.Executives));
	}

	[Fact]
	public async Task A_page_without_a_beginning_has_no_period()
	{
		var untold = await CreateLoreAsync(null, "Untold Age", null);
		await WorkDayAsync(2);

		var listing = await Polaris(api => api.ListForLoreAsync(untold.Puck!, Ct));

		Assert.NotNull(listing);
		Assert.Null(listing.Beginning);
		Assert.Null(listing.Through);
		Assert.Empty(listing.Cycles);
	}

	[Fact]
	public async Task A_page_that_has_not_begun_lists_nothing_yet()
	{
		var coming = await CreateLoreAsync(null, "Coming Age", 5);
		await Polaris(api => api.PlanAsync(Today, 6, cancellationToken: Ct));

		var listing = await Polaris(api => api.ListForLoreAsync(coming.Puck!, Ct));

		Assert.NotNull(listing);
		Assert.Equal(Today.AddDays(5), listing.Beginning);
		Assert.Equal(Today, listing.Through);
		Assert.Empty(listing.Cycles);
	}

	[Fact]
	public async Task An_unknown_page_is_not_found()
	{
		Assert.Null(await Polaris(api => api.ListForLoreAsync("Era99", Ct)));
	}

	[Fact]
	public void A_period_ends_at_the_earliest_closing_on_the_lineage()
	{
		LorePage Page(string id, string? parentId, int daysAgo) => new() { Id = id, ParentId = parentId, Title = id, Level = "Era", Beginning = Today.AddDays(-daysAgo) };
		var pages = new[]
		{
			Page("Era1", null, 100),
			Page("Era2", null, 30),
			Page("Era1/Cha1", "Era1", 100),
			Page("Era1/Cha2", "Era1", 60),
			Page("Era1/Cha2/Act1", "Era1/Cha2", 60),
			Page("Era1/Cha2/Act2", "Era1/Cha2", 45),
		};

		// The last Act of the last Chapter of a concluded Era: neither it nor its Chapter has a later sibling, so the
		// Era's own end is what closes it.
		var period = LorePage.ResolveActivePeriod(pages[^1], pages);
		Assert.Equal(new LoreActivePeriod(Today.AddDays(-45), Today.AddDays(-30)), period);

		// An Act closed by its own sibling keeps that earlier end.
		Assert.Equal(Today.AddDays(-45), LorePage.ResolveActivePeriod(pages[4], pages)!.EndingExclusive);

		// The latest Era runs on.
		Assert.Null(LorePage.ResolveActivePeriod(pages[1], pages)!.EndingExclusive);
	}
}
