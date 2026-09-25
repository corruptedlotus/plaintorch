using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Calendar;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A timeframe's orbit is anchored by a persisted <see cref="TimeframeOrbitScheduleState"/> (PEP100 patch 2), following
/// the declarative epoch policy: setting an orbit creates a state anchored today (or at a fixed <c>Z{…}</c> date, read
/// on the vault default calendar the orbit is resolved on), changing it re-anchors, clearing it removes the state, and the state dies with its timeframe — also when a lunar
/// directive delete cascades the timeframe away. Day matching is a preview that never advances the state; a legacy
/// timeframe without a state gets one lazily, and an unreadable orbit reads as not matching instead of throwing.
/// Incentive states keep living beside them in the same table.
/// </summary>
public sealed class TimeframeOrbitStateTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private async Task<(LunarDirective Lunar, Timeframe Timeframe)> CreateTimeframeAsync(string? orbit)
	{
		var lunar = await Directives(api => api.CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var timeframe = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Morning", new TimeOnly(8, 0), new TimeOnly(12, 0), Orbit: orbit), Ct));
		return (lunar, timeframe);
	}

	private Task<List<TimeframeOrbitScheduleState>> StatesOfAsync(long timeframeId)
		=> Vault.QueryAsync(context => context.TimeframeOrbitScheduleStates.AsNoTracking().Where(state => state.TimeframeId == timeframeId).ToListAsync(Ct));

	private static DateOnly EpochOf(TimeframeOrbitScheduleState state)
		=> DateOnly.FromDateTime(DateTimeOffset.Parse(OrbitSnapshot.FromJson(state.StateJson).Epoch, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime);

	/// <summary>The civil day of a Pleiadean date, computed by the calendar class independent of the orbit engine.</summary>
	private static DateOnly PleiadeanDay(int year, int month, int day)
		=> DateOnly.FromDateTime(new PleiadeanCalendar(year, month, day).ToDateTime());

	private Task<bool> MatchesAsync(long timeframeId, DateOnly day)
		=> Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			var timeframe = await context.Timeframes.AsNoTracking().SingleAsync(item => item.Id == timeframeId, Ct);
			var matches = await services.GetRequiredService<PlaintorchOrbitService>().MatchesTimeframeDayAsync(timeframe, day, Ct);
			await context.SaveChangesAsync(Ct);
			return matches;
		});

	/// <summary>
	/// Asserts an epoch the core stamped from its own clock during a call: it lies between the day read before the call
	/// and the day read after it, so a run crossing midnight still passes.
	/// </summary>
	private static void AssertAnchoredDuring(DateOnly before, DateOnly after, DateOnly epoch)
	{
		Assert.InRange(epoch, before, after);
	}

	[Fact]
	public async Task Setting_an_orbit_creates_a_state_anchored_today()
	{
		var before = Today;
		var (_, timeframe) = await CreateTimeframeAsync("w[d{1~5}]");
		var after = Today;

		var state = Assert.Single(await StatesOfAsync(timeframe.Id));
		AssertAnchoredDuring(before, after, EpochOf(state));
		Assert.Equal("w[d{1~5}]", OrbitSnapshot.FromJson(state.StateJson).Notation);

		// An orbit set later through an update anchors the same way.
		var (_, unscheduled) = await CreateTimeframeAsync(null);
		Assert.Empty(await StatesOfAsync(unscheduled.Id));
		before = Today;
		await Directives(api => api.UpdateTimeframeAsync(unscheduled.Id, new TimeframeUpdate(Orbit: "d"), Ct));
		after = Today;
		AssertAnchoredDuring(before, after, EpochOf(Assert.Single(await StatesOfAsync(unscheduled.Id))));
	}

	[Fact]
	public async Task A_fixed_date_literal_anchors_at_its_own_date_on_a_gregorian_vault()
	{
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.DefaultCalendar, DeclarativeCalendar.Gregorian, Ct));

		var (_, timeframe) = await CreateTimeframeAsync("Z{2027/6/5}");

		Assert.Equal(new DateOnly(2027, 6, 5), EpochOf(Assert.Single(await StatesOfAsync(timeframe.Id))));
	}

	[Fact]
	public async Task A_pleiadean_only_day_literal_saves_and_anchors_at_that_pleiadean_dates_civil_day()
	{
		// The vault default calendar is Pleiadean, whose months run 60–61 days: the 40th of the third month is a real
		// date there and none at all in Gregorian, so reading the literal as Gregorian used to fail the save.
		var (_, timeframe) = await CreateTimeframeAsync("Z{3/3/40}");

		Assert.Equal(PleiadeanDay(3, 3, 40), EpochOf(Assert.Single(await StatesOfAsync(timeframe.Id))));

		// An update to another Pleiadean-only day re-anchors the same way.
		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Orbit: "Z{3/4/55}"), Ct));
		Assert.Equal(PleiadeanDay(3, 4, 55), EpochOf(Assert.Single(await StatesOfAsync(timeframe.Id))));
	}

	[Fact]
	public async Task An_in_range_pleiadean_literal_anchors_on_exactly_the_day_the_engine_selects()
	{
		var day = PleiadeanDay(3, 3, 10);
		var (_, timeframe) = await CreateTimeframeAsync("Z{3/3/10}");

		Assert.Equal(day, EpochOf(Assert.Single(await StatesOfAsync(timeframe.Id))));
		Assert.True(await MatchesAsync(timeframe.Id, day));
		Assert.False(await MatchesAsync(timeframe.Id, day.AddDays(-1)));
		Assert.False(await MatchesAsync(timeframe.Id, day.AddDays(1)));
	}

	[Fact]
	public async Task A_literal_naming_a_day_the_calendar_lacks_saves_and_anchors_like_a_recurring_orbit()
	{
		var before = Today;
		var (_, timeframe) = await CreateTimeframeAsync("Z{3/3/62}");
		var after = Today;

		// No open cycle, so the fallback anchor is today; the engine never selects the impossible day either.
		AssertAnchoredDuring(before, after, EpochOf(Assert.Single(await StatesOfAsync(timeframe.Id))));
	}

	[Fact]
	public async Task Changing_the_orbit_resets_the_state_and_clearing_it_removes_the_state()
	{
		var (_, timeframe) = await CreateTimeframeAsync("d");
		var original = Assert.Single(await StatesOfAsync(timeframe.Id));

		// An update that leaves the orbit alone keeps the very same state row.
		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Title: "Dawn"), Ct));
		Assert.Equal(original.Id, Assert.Single(await StatesOfAsync(timeframe.Id)).Id);

		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Orbit: "w[d{1}]"), Ct));
		var reset = Assert.Single(await StatesOfAsync(timeframe.Id));
		Assert.NotEqual(original.Id, reset.Id);
		Assert.Equal("w[d{1}]", OrbitSnapshot.FromJson(reset.StateJson).Notation);

		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Orbit: (string?)null), Ct));
		Assert.Empty(await StatesOfAsync(timeframe.Id));
	}

	[Fact]
	public async Task A_state_dies_with_its_timeframe()
	{
		var (_, timeframe) = await CreateTimeframeAsync("d");
		Assert.Single(await StatesOfAsync(timeframe.Id));

		await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteTimeframeAsync(timeframe.Id, Ct));

		Assert.Empty(await StatesOfAsync(timeframe.Id));
	}

	[Fact]
	public async Task A_state_dies_when_a_lunar_directive_delete_cascades_its_timeframes()
	{
		var (lunar, timeframe) = await CreateTimeframeAsync("d");
		Assert.Single(await StatesOfAsync(timeframe.Id));

		await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteAsync(lunar.Id, Ct));

		Assert.Equal(0, await Vault.QueryAsync(context => context.Timeframes.CountAsync(item => item.Id == timeframe.Id, Ct)));
		Assert.Empty(await StatesOfAsync(timeframe.Id));
	}

	[Fact]
	public async Task An_interval_orbit_counts_from_the_day_it_was_set_and_previews_never_advance_the_state()
	{
		var dayBefore = Today;
		var (_, timeframe) = await CreateTimeframeAsync("d%2");
		var dayAfter = Today;
		var state = Assert.Single(await StatesOfAsync(timeframe.Id));
		var before = state.StateJson;

		// Counted from the epoch actually stamped, so a run crossing midnight reads the same days.
		var setOn = EpochOf(state);
		AssertAnchoredDuring(dayBefore, dayAfter, setOn);
		Assert.True(await MatchesAsync(timeframe.Id, setOn));
		Assert.False(await MatchesAsync(timeframe.Id, setOn.AddDays(1)));
		Assert.True(await MatchesAsync(timeframe.Id, setOn.AddDays(2)));

		// Matching is a preview: the persisted state is exactly what the orbit reset wrote.
		Assert.Equal(before, Assert.Single(await StatesOfAsync(timeframe.Id)).StateJson);
	}

	[Fact]
	public async Task A_timeframe_without_an_orbit_matches_every_day_and_keeps_no_state()
	{
		var (_, timeframe) = await CreateTimeframeAsync(null);

		Assert.True(await MatchesAsync(timeframe.Id, Today));
		Assert.True(await MatchesAsync(timeframe.Id, Today.AddDays(3)));
		Assert.Empty(await StatesOfAsync(timeframe.Id));
	}

	[Fact]
	public async Task A_legacy_timeframe_without_a_state_gets_one_lazily_and_keeps_it()
	{
		var (_, timeframe) = await CreateTimeframeAsync("d%2");
		// Data from before timeframe states existed: an orbit, but no state row.
		await Vault.QueryAsync(context => context.TimeframeOrbitScheduleStates.Where(state => state.TimeframeId == timeframe.Id).ExecuteDeleteAsync(Ct));

		var pastDay = Today.AddDays(-5);
		Assert.True(await MatchesAsync(timeframe.Id, pastDay));

		// A past day anchors the lazy state at that day (the incentive lazy rule), and the row is saved so later
		// evaluations reuse the same epoch and seed rather than re-anchoring each time.
		var lazy = Assert.Single(await StatesOfAsync(timeframe.Id));
		Assert.Equal(pastDay, EpochOf(lazy));
		Assert.False(await MatchesAsync(timeframe.Id, pastDay.AddDays(1)));
		Assert.Equal(lazy.Id, Assert.Single(await StatesOfAsync(timeframe.Id)).Id);
	}

	[Fact]
	public async Task An_unreadable_stored_orbit_reads_as_not_matching_instead_of_throwing()
	{
		var (_, timeframe) = await CreateTimeframeAsync(null);
		// Only a hand-edited database can hold an invalid orbit; the API validates every one it writes.
		await Vault.QueryAsync(context => context.Timeframes.Where(item => item.Id == timeframe.Id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Orbit, "w[d{"), Ct));

		Assert.False(await MatchesAsync(timeframe.Id, Today));
		Assert.Empty(await StatesOfAsync(timeframe.Id));
	}

	[Fact]
	public async Task Incentive_states_live_beside_timeframe_states()
	{
		var (_, timeframe) = await CreateTimeframeAsync("d");
		var decree = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Stretch", Orbit: "d"), Ct));

		var incentiveState = await Vault.QueryAsync(context => context.IncentiveOrbitScheduleStates.AsNoTracking().SingleAsync(state => state.IncentiveId == decree.Id, Ct));
		Assert.Equal("d", OrbitSnapshot.FromJson(incentiveState.StateJson).Notation);

		// Both kinds share one table and are told apart by their kind alone.
		var all = await Vault.QueryAsync(context => context.OrbitScheduleStates.AsNoTracking().ToListAsync(Ct));
		Assert.Contains(all, state => state is IncentiveOrbitScheduleState { IncentiveId: var id } && id == decree.Id);
		Assert.Contains(all, state => state is TimeframeOrbitScheduleState { TimeframeId: var id } && id == timeframe.Id);
	}
}
