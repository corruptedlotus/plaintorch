using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A begun Polaris cycle's timeframe candidates are computed once and cached (PEP100 patch 2): every timeframe without an
/// orbit, plus every timeframe whose orbit selects the cycle's local start day on the vault default calendar — that day
/// stays the cycle's day past midnight, and an orbit set while that cycle is open anchors no later than its day, so it can
/// still select it after midnight. The cache is warmed when a cycle begins and when the core activates a vault over
/// an already-begun cycle, purged at a vault session boundary, and invalidated by every write that can change the set —
/// timeframe edits mid-cycle, a cycle begun, ended or deleted (also through its note), a lunar directive delete, and the
/// default-calendar preference (set or reset) — while unrelated writes keep it warm. A recompute racing an invalidation,
/// or the preference store update, never stores a stale result, and a warm is best effort: it never fails a begin or a
/// vault activation, and a lost race pinning a lazy orbit state still yields the computed candidates.
/// </summary>
/// <remarks>
/// Days are derived from the cycle each test actually started (or from one captured value), never read afresh from the
/// wall clock, so a run that crosses midnight cannot disagree with itself.
/// </remarks>
public sealed class TimeframeCandidateTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static DateTimeOffset TodayAt(int hour, int minute)
		=> new(DateTime.Today.AddHours(hour).AddMinutes(minute));

	/// <summary>The cycle's local start day, derived independently of the service under test.</summary>
	private static DateOnly DayOf(PolarisCycle cycle) => DateOnly.FromDateTime(cycle.StartTime!.Value.LocalDateTime);

	// The Pleiadean week starts on Saturday (d{1} = Saturday); the Gregorian week on Monday (d{1} = Monday).
	private static int PleiadeanWeekday(DateOnly day) => ((int)day.DayOfWeek + 1) % 7 + 1;

	private static int GregorianWeekday(DateOnly day) => day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;

	private TimeframeCandidateCache Cache => Vault.GetSingleton<TimeframeCandidateCache>();

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<PolarisCycle> StartCycleAsync()
		=> Polaris(api => api.StartNewAsync(TodayAt(0, 5), cancellationToken: Ct));

	private async Task<(LunarDirective Lunar, Timeframe Timeframe)> CreateTimeframeAsync(string title, string? orbit, string lunarTitle = "Moon Law")
	{
		var lunar = await Directives(api => api.CreateLunarAsync(lunarTitle, cancellationToken: Ct));
		// An all-day window (00:00 to 00:00 is never active, so 00:00-23:59) keeps the active listing about candidacy.
		var timeframe = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan(title, new TimeOnly(0, 0), new TimeOnly(23, 59), Orbit: orbit), Ct));
		return (lunar, timeframe);
	}

	private async Task<IReadOnlyList<long>> ActiveIdsAsync()
		=> (await Directives(api => api.ListActiveTimeframesAsync(TodayAt(12, 0), Ct))).Select(record => record.Id).ToList();

	private Task SetDefaultCalendarAsync(DeclarativeCalendar calendar)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.DefaultCalendar, calendar, Ct));

	private Task<List<(long Id, long TimeframeId, string StateJson)>> TimeframeStatesAsync()
		=> Vault.QueryAsync(async context => (await context.TimeframeOrbitScheduleStates.AsNoTracking().OrderBy(state => state.Id).ToListAsync(Ct))
			.Select(state => (state.Id, state.TimeframeId, state.StateJson))
			.ToList());

	private static DateOnly EpochOf(string stateJson)
		=> DateOnly.FromDateTime(DateTimeOffset.Parse(OrbitSnapshot.FromJson(stateJson).Epoch, CultureInfo.InvariantCulture).UtcDateTime);

	/// <summary>
	/// Re-anchors every timeframe orbit state a week before <paramref name="day"/>, as if the orbits had been set well
	/// before the cycle began. The product can never produce that for a BACKDATED cycle: an orbit set while no cycle is
	/// open anchors today, and only a cycle open at the time of the set pulls the anchor back to its day. So this is
	/// only for a test whose timeframes must exist before a cycle it begins in the past. The rewrite is not a candidate
	/// trigger, so it runs before anything is read.
	/// </summary>
	private Task AnchorTimeframeStatesBeforeAsync(DateOnly day)
		=> Vault.QueryAsync(async context =>
		{
			foreach (var state in await context.TimeframeOrbitScheduleStates.ToListAsync(Ct))
			{
				state.StateJson = OrbitDays.CreateState(OrbitSnapshot.FromJson(state.StateJson).Notation, day.AddDays(-7), OrbitDays.Pleiadean);
			}

			return await context.SaveChangesAsync(Ct);
		});

	/// <summary>Stores an entry for a cycle that is not the active one, so any invalidation shows as an empty cache.</summary>
	private Task PrewarmUnrelatedEntryAsync()
		=> Cache.GetOrComputeAsync("unrelated-cycle", TodayAt(0, 1), DateOnly.FromDateTime(DateTime.Today), _ => Task.FromResult<IReadOnlyList<long>>([]), cancellationToken: Ct);

	[Fact]
	public async Task A_timeframe_without_an_orbit_is_a_candidate_of_every_cycle()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Anytime", null);
		var cycle = await StartCycleAsync();

		var entry = Assert.IsType<TimeframeCandidateCache.Entry>(Cache.Find(cycle.Id, cycle.StartTime!.Value));
		Assert.Equal(DayOf(cycle), entry.Day);
		Assert.Equal([timeframe.Id], entry.CandidateIds);
	}

	[Fact]
	public async Task A_weekday_orbit_reads_on_the_default_calendar_and_flips_with_the_preference_without_resetting_states()
	{
		var cycle = await StartCycleAsync();
		var day = DayOf(cycle);
		var (_, pleiadean) = await CreateTimeframeAsync("Pleiadean weekday", $"w[d{{{PleiadeanWeekday(day)}}}]");
		var (_, gregorian) = await CreateTimeframeAsync("Gregorian weekday", $"w[d{{{GregorianWeekday(day)}}}]", "Sun Law");

		// The vault default is Pleiadean: its Saturday-first week names the cycle day by the Pleiadean index only.
		Assert.Equal([pleiadean.Id], await ActiveIdsAsync());
		Assert.Equal([pleiadean.Id], Cache.Find(cycle.Id, cycle.StartTime!.Value)!.CandidateIds);
		var statesBefore = await TimeframeStatesAsync();
		Assert.Equal([pleiadean.Id, gregorian.Id], statesBefore.Select(state => state.TimeframeId).Order());

		// Flipping the default to Gregorian invalidates the cycle's candidates, and the recompute reads the Monday-first
		// week instead.
		await SetDefaultCalendarAsync(DeclarativeCalendar.Gregorian);
		Assert.Null(Cache.Current);
		Assert.Equal([gregorian.Id], await ActiveIdsAsync());

		// A calendar change never re-anchors an orbit (D3): the very same state rows, with the same epoch and seed.
		Assert.Equal(statesBefore, await TimeframeStatesAsync());
	}

	[Fact]
	public async Task A_cycle_begun_yesterday_evening_keeps_its_start_day_past_midnight()
	{
		// Captured once: the start is explicit, and the listing's candidate day comes from the cycle, not the clock.
		var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
		var today = yesterday.AddDays(1);
		var (_, yesterdays) = await CreateTimeframeAsync("Yesterday's weekday", $"w[d{{{PleiadeanWeekday(yesterday)}}}]");
		var (_, todays) = await CreateTimeframeAsync("Today's weekday", $"w[d{{{PleiadeanWeekday(today)}}}]", "Sun Law");
		// Genuine simulation: both orbits must predate a cycle begun in the past, and with no cycle open the core anchors
		// them today, after yesterday — nothing the product does can set an orbit before a backdated begin.
		await AnchorTimeframeStatesBeforeAsync(yesterday);

		var cycle = await Polaris(api => api.StartNewAsync(new DateTimeOffset(yesterday.ToDateTime(new TimeOnly(20, 0))), cancellationToken: Ct));
		var entry = Assert.IsType<TimeframeCandidateCache.Entry>(Cache.Find(cycle.Id, cycle.StartTime!.Value));
		Assert.Equal(yesterday, entry.Day);
		Assert.Equal([yesterdays.Id], entry.CandidateIds);

		// Listing at today's noon still serves the start day's set, never today's weekday.
		var at = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)));
		Assert.Equal([yesterdays.Id], (await Directives(api => api.ListActiveTimeframesAsync(at, Ct))).Select(record => record.Id));

		// A warm at vault activation reads the same start day.
		await Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct));
		entry = Assert.IsType<TimeframeCandidateCache.Entry>(Cache.Find(cycle.Id, cycle.StartTime!.Value));
		Assert.Equal(yesterday, entry.Day);
		Assert.Equal([yesterdays.Id], entry.CandidateIds);
		Assert.DoesNotContain(todays.Id, entry.CandidateIds);
	}

	[Fact]
	public async Task An_orbit_set_after_midnight_under_a_cycle_begun_yesterday_evening_anchors_on_the_cycle_day()
	{
		// The cycle opened yesterday evening and is still open today; both orbits are set now, after its day.
		var cycle = await Polaris(api => api.StartNewAsync(new DateTimeOffset(DateTime.Today.AddDays(-1).AddHours(20)), cancellationToken: Ct));
		var cycleDay = DayOf(cycle);
		var (_, created) = await CreateTimeframeAsync("Cycle weekday", $"w[d{{{PleiadeanWeekday(cycleDay)}}}]");
		var (_, edited) = await CreateTimeframeAsync("Alternate days", null, "Sun Law");
		await Directives(api => api.UpdateTimeframeAsync(edited.Id, new TimeframeUpdate(Orbit: "d%2"), Ct));

		// Anchored at today they would yield nothing on the cycle day; anchored at the open cycle's day, both select it.
		var at = new DateTimeOffset(cycleDay.AddDays(1).ToDateTime(new TimeOnly(12, 0)));
		Assert.Equal(
			[created.Id, edited.Id],
			(await Directives(api => api.ListActiveTimeframesAsync(at, Ct))).Select(record => record.Id).Order());
		var states = await TimeframeStatesAsync();
		Assert.Equal([created.Id, edited.Id], states.Select(state => state.TimeframeId).Order());
		Assert.All(states, state => Assert.Equal(cycleDay, EpochOf(state.StateJson)));
	}

	[Fact]
	public async Task Starting_a_cycle_warms_its_candidates_and_ending_it_leaves_nothing_active()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Anytime", null);
		Assert.Null(Cache.Current);

		var cycle = await StartCycleAsync();
		Assert.NotNull(Cache.Find(cycle.Id, cycle.StartTime!.Value));
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());

		await Polaris(api => api.EndAsync(cancellationToken: Ct));

		// The end invalidates the cache, and an ended cycle has no active timeframes at all.
		Assert.Null(Cache.Current);
		Assert.Empty(await ActiveIdsAsync());
	}

	[Fact]
	public async Task Timeframes_created_edited_and_deleted_mid_cycle_are_reflected()
	{
		var cycle = await StartCycleAsync();
		Assert.Empty(await ActiveIdsAsync());

		var (lunar, timeframe) = await CreateTimeframeAsync("Anytime", null);
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());

		// An orbit that never selects the cycle day (the next weekday) drops it; one that does brings it back.
		var nextDay = DayOf(cycle).AddDays(1);
		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Orbit: $"w[d{{{PleiadeanWeekday(nextDay)}}}]"), Ct));
		Assert.Empty(await ActiveIdsAsync());
		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Orbit: "d"), Ct));
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());

		// A rename is projected fresh on read, never served from a stale cached record.
		await Directives(api => api.UpdateTimeframeAsync(timeframe.Id, new TimeframeUpdate(Title: "Always"), Ct));
		Assert.Equal("Always", Assert.Single(await Directives(api => api.ListActiveTimeframesAsync(TodayAt(12, 0), Ct))).Title);

		await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteTimeframeAsync(timeframe.Id, Ct));
		Assert.Empty(await ActiveIdsAsync());

		// A lunar directive delete cascades its timeframes away in the database, with no tracked timeframe entry; the
		// directive delete itself invalidates the set.
		var cascaded = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Late", new TimeOnly(0, 0), new TimeOnly(23, 59)), Ct));
		Assert.Equal([cascaded.Id], await ActiveIdsAsync());
		await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteAsync(lunar.Id, Ct));
		Assert.Null(Cache.Current);
		Assert.Empty(await ActiveIdsAsync());
	}

	[Fact]
	public async Task A_cycle_begun_and_ended_through_its_note_invalidates_through_the_save_hook()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Anytime", null);
		var planned = await Polaris(api => api.PlanAsync(DateOnly.FromDateTime(DateTime.Today).AddDays(-1), 1, cancellationToken: Ct));
		Assert.Null(planned.StartTime);
		Assert.Empty(await ActiveIdsAsync());

		var note = Vault.MarkdownFilesUnder(Vault.VaultRoot).Single(file => file.Contains(planned.Id, StringComparison.OrdinalIgnoreCase));
		var start = new DateTimeOffset(DateTime.Today.AddMinutes(5));

		// Begin the cycle by writing its start time into the note's frontmatter and replaying the watcher event; this
		// bypasses the API activation path. A pre-warmed entry proves the watcher's save itself invalidated the cache:
		// it is gone before anything reads the listing (a read would otherwise just recompute on its own miss).
		await PrewarmUnrelatedEntryAsync();
		Assert.NotNull(Cache.Current);
		File.WriteAllText(note, SetFrontmatterField(File.ReadAllText(note), "startTime", start));
		await Vault.ReconcileAsync(note);
		Assert.Null(Cache.Current);

		var begun = await Vault.QueryAsync(context => context.PolarisCycles.AsNoTracking().SingleAsync(item => item.Id == planned.Id, Ct));
		Assert.NotNull(begun.StartTime);
		Assert.Null(begun.EndTime);
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());
		Assert.NotNull(Cache.Find(begun.Id, begun.StartTime!.Value));

		File.WriteAllText(note, SetFrontmatterField(File.ReadAllText(note), "endTime", start.AddHours(8)));
		await Vault.ReconcileAsync(note);

		Assert.Null(Cache.Current);
		Assert.Empty(await ActiveIdsAsync());
	}

	[Fact]
	public async Task Vault_activation_warms_the_candidates_of_a_cycle_that_had_already_begun()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Anytime", null);
		var cycle = await StartCycleAsync();

		// A fresh boot: nothing cached from before, then the activation pipeline runs over the already-begun cycle.
		Cache.Purge();
		Assert.Null(Cache.Current);
		await Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct));

		var entry = Assert.IsType<TimeframeCandidateCache.Entry>(Cache.Find(cycle.Id, cycle.StartTime!.Value));
		Assert.Equal([timeframe.Id], entry.CandidateIds);
	}

	[Fact]
	public async Task Vault_activation_purges_and_leaves_the_cache_cold_without_an_active_cycle()
	{
		await CreateTimeframeAsync("Anytime", null);
		await StartCycleAsync();
		await Polaris(api => api.EndAsync(cancellationToken: Ct));
		await ActiveIdsAsync();

		await Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct));

		Assert.Null(Cache.Current);
	}

	[Fact]
	public async Task A_legacy_orbit_without_state_is_anchored_and_saved_by_the_candidate_computation()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Alternate days", "d%2");
		await Vault.QueryAsync(context => context.TimeframeOrbitScheduleStates.Where(state => state.TimeframeId == timeframe.Id).ExecuteDeleteAsync(Ct));

		var cycle = await StartCycleAsync();

		// The lazy state is anchored on the cycle day, so an every-other-day orbit selects it, and the row is persisted.
		Assert.Equal([timeframe.Id], Cache.Find(cycle.Id, cycle.StartTime!.Value)!.CandidateIds);
		Assert.Equal(1, await Vault.QueryAsync(context => context.TimeframeOrbitScheduleStates.CountAsync(state => state.TimeframeId == timeframe.Id, Ct)));
	}

	[Fact]
	public async Task A_lost_race_pinning_a_lazy_state_still_yields_the_computed_candidates()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Alternate days", "d%2");
		await Vault.QueryAsync(context => context.TimeframeOrbitScheduleStates.Where(state => state.TimeframeId == timeframe.Id).ExecuteDeleteAsync(Ct));

		// Stand in for the concurrent writer (a delete or an orbit edit committing first): the database refuses the lazy
		// state's insert exactly the way a violated key would, deterministically.
		var trigger = "CREATE TRIGGER refuse_lazy_state BEFORE INSERT ON OrbitScheduleStates WHEN NEW.TimeframeId = "
			+ timeframe.Id.ToString(CultureInfo.InvariantCulture)
			+ " BEGIN SELECT RAISE(ABORT, 'a concurrent write got there first'); END;";
		await Vault.QueryAsync(context => context.Database.ExecuteSqlRawAsync(trigger, Ct));

		// The begin (whose warm hits the refused save) succeeds, and the warm still stores the computed candidates.
		var cycle = await StartCycleAsync();
		Assert.NotNull(cycle.StartTime);
		Assert.Equal([timeframe.Id], Cache.Find(cycle.Id, cycle.StartTime!.Value)!.CandidateIds);
		Assert.Empty(await TimeframeStatesAsync());

		// A lazy recompute in a fresh scope hits the same refusal and still answers.
		Cache.Invalidate();
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());

		await Vault.QueryAsync(context => context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_lazy_state;", Ct));
		Cache.Invalidate();
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());
		Assert.Equal(timeframe.Id, Assert.Single(await TimeframeStatesAsync()).TimeframeId);
	}

	[Fact]
	public async Task A_failed_warm_never_fails_the_begin_or_the_vault_activation()
	{
		var (_, timeframe) = await CreateTimeframeAsync("Anytime", null);

		// Corrupt the timeframe row so the candidate computation itself throws while reading it, then repair it.
		await CorruptTimeframeAsync(timeframe.Id, corrupt: true);
		PolarisCycle cycle;
		try
		{
			cycle = await StartCycleAsync();
			await Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct));
		}
		finally
		{
			await CorruptTimeframeAsync(timeframe.Id, corrupt: false);
		}

		// The begin was committed and written despite the failed warms; the listing recomputes on its miss.
		Assert.NotNull(cycle.StartTime);
		var stored = await Vault.QueryAsync(context => context.PolarisCycles.AsNoTracking().SingleAsync(item => item.Id == cycle.Id, Ct));
		Assert.NotNull(stored.StartTime);
		Assert.Contains(Vault.MarkdownFilesUnder(Vault.VaultRoot), file => file.Contains(cycle.Id, StringComparison.OrdinalIgnoreCase)
			&& File.ReadAllText(file).Contains("startTime:", StringComparison.Ordinal));
		Assert.Null(Cache.Current);
		Assert.Equal([timeframe.Id], await ActiveIdsAsync());
	}

	[Fact]
	public async Task A_recompute_right_after_the_calendar_preference_call_sees_the_new_calendar()
	{
		var cycle = await StartCycleAsync();
		var day = DayOf(cycle);
		var (_, pleiadean) = await CreateTimeframeAsync("Pleiadean weekday", $"w[d{{{PleiadeanWeekday(day)}}}]");
		var (_, gregorian) = await CreateTimeframeAsync("Gregorian weekday", $"w[d{{{GregorianWeekday(day)}}}]", "Sun Law");
		Assert.Equal([pleiadean.Id], await ActiveIdsAsync());

		// A reader racing the preference write: it recomputes after the save hook's invalidation but before the store
		// holds the new value, so it reads the old (Pleiadean) calendar and stores that set as current.
		IReadOnlyList<long>? racedIds = null;
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.SavedChanges += (_, _) => racedIds = Vault.WithScopeAsync(racing =>
				racing.GetRequiredService<TimeframeCandidateService>().GetCandidateIdsAsync(cycle, Ct)).GetAwaiter().GetResult();
			await services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.DefaultCalendar, DeclarativeCalendar.Gregorian, Ct);
		});
		Assert.Equal([pleiadean.Id], racedIds);

		// The store write-through invalidated once the new calendar was visible, so the next recompute reads it.
		Assert.Null(Cache.Current);
		Assert.Equal([gregorian.Id], await ActiveIdsAsync());
	}

	[Fact]
	public async Task Resetting_the_default_calendar_invalidates_the_candidates()
	{
		await CreateTimeframeAsync("Anytime", null);
		await SetDefaultCalendarAsync(DeclarativeCalendar.Gregorian);
		await StartCycleAsync();
		Assert.NotNull(Cache.Current);

		// Through the preference service (row deleted, store cleared).
		Assert.True(await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().ResetAsync(PreferenceKeys.DefaultCalendar, Ct)));
		Assert.Null(Cache.Current);

		// And through the save hook alone: a bare row delete, with no store write-through at all.
		await SetDefaultCalendarAsync(DeclarativeCalendar.Gregorian);
		await ActiveIdsAsync();
		Assert.NotNull(Cache.Current);
		await Vault.QueryAsync(async context =>
		{
			context.UserPreferences.Remove(await context.UserPreferences.SingleAsync(row => row.Key == PreferenceKeys.DefaultCalendar, Ct));
			return await context.SaveChangesAsync(Ct);
		});
		Assert.Null(Cache.Current);
	}

	[Fact]
	public async Task Deleting_a_polaris_cycle_invalidates_the_candidates()
	{
		await CreateTimeframeAsync("Anytime", null);
		var tomorrow = await Polaris(api => api.PlanAsync(DateOnly.FromDateTime(DateTime.Today), 1, cancellationToken: Ct));
		await StartCycleAsync();
		Assert.NotNull(Cache.Current);

		await Vault.QueryAsync(async context =>
		{
			context.PolarisCycles.Remove(await context.PolarisCycles.SingleAsync(cycle => cycle.Id == tomorrow.Id, Ct));
			return await context.SaveChangesAsync(Ct);
		});

		Assert.Null(Cache.Current);
	}

	[Fact]
	public async Task Unrelated_writes_keep_the_warm_candidates()
	{
		var (lunar, timeframe) = await CreateTimeframeAsync("Anytime", null);
		var cycle = await StartCycleAsync();
		var warm = Assert.IsType<TimeframeCandidateCache.Entry>(Cache.Find(cycle.Id, cycle.StartTime!.Value));

		// Another preference key, and a directive rename (modified, not deleted), change nothing about candidacy.
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.CalDavFloatingRender, CalDavFloatingRender.AllDay, Ct));
		Assert.Same(warm, Cache.Current);
		await Directives(api => api.UpdateLunarAsync(lunar.Id, new LunarDirectiveUpdate(Title: "Tide Law"), Ct));
		Assert.Same(warm, Cache.Current);

		// The listing still projects the directive's new title from the warm ids.
		var record = Assert.Single(await Directives(api => api.ListActiveTimeframesAsync(TodayAt(12, 0), Ct)));
		Assert.Equal(timeframe.Id, record.Id);
		Assert.Equal("Tide Law", record.DirectiveTitle);
		Assert.Same(warm, Cache.Current);
	}

	[Fact]
	public async Task A_recompute_racing_an_invalidation_never_stores_its_stale_result()
	{
		var cache = new TimeframeCandidateCache();
		var start = TodayAt(0, 5);
		var day = DateOnly.FromDateTime(start.LocalDateTime);

		var ids = await cache.GetOrComputeAsync("cycle", start, day, _ =>
		{
			// A write lands while the recompute is still reading.
			cache.Invalidate();
			return Task.FromResult<IReadOnlyList<long>>([1L]);
		}, cancellationToken: Ct);

		// The caller still gets its answer, but the cache stays invalidated.
		Assert.Equal([1L], ids);
		Assert.Null(cache.Current);

		var stored = await cache.GetOrComputeAsync("cycle", start, day, _ => Task.FromResult<IReadOnlyList<long>>([2L]), cancellationToken: Ct);
		Assert.Equal([2L], stored);
		Assert.Equal([2L], cache.Find("cycle", start)!.CandidateIds);

		// A re-begun cycle (a different start) misses the entry; a purge empties it.
		Assert.Null(cache.Find("cycle", start.AddMinutes(1)));
		cache.Purge();
		Assert.Null(cache.Current);
	}

	/// <summary>
	/// Writes an unreadable start time into a timeframe row (or restores its midnight start), so materializing that
	/// timeframe fails outright — a broken database read during a warm.
	/// </summary>
	private Task CorruptTimeframeAsync(long timeframeId, bool corrupt)
		=> Vault.QueryAsync(context => context.Database.ExecuteSqlRawAsync(
			"UPDATE Timeframes SET StartTime = {0} WHERE Id = {1}",
			[corrupt ? "not a time" : "00:00:00", timeframeId],
			Ct));

	/// <summary>
	/// Sets (or replaces) a top-level frontmatter field with an ISO-8601 date-time value.
	/// </summary>
	private static string SetFrontmatterField(string content, string field, DateTimeOffset value)
	{
		var line = $"{field}: {value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)}";
		var pattern = new Regex($"^{Regex.Escape(field)}:[^\\r\\n]*", RegexOptions.Multiline);
		if (pattern.IsMatch(content))
		{
			return pattern.Replace(content, line.Replace("$", "$$", StringComparison.Ordinal), 1);
		}

		var firstFence = content.IndexOf("---", StringComparison.Ordinal);
		var insertAt = content.IndexOf('\n', firstFence) + 1;
		return content.Insert(insertAt, line + Environment.NewLine);
	}
}
