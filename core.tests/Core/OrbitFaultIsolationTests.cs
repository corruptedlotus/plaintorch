using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers orbit fault isolation (PEP103 I-124, user ruling 2026-10-30). A stored orbit that no longer parses or builds —
/// written here past validation, into both the owner's column and the notation its schedule state resumes from — fails
/// only its own owner: the rolling harden-on-time pass still hardens every other fate and decree, the agenda still lists
/// them, a Polaris cycle begin still completes and reflects the other reflect-decrees, the timeframe candidates still
/// hold the other timeframes, and a save that merely touches the faulty owner still goes through. The faulty owner
/// projects nothing, caches no next occurrence, keeps its cursor, and is logged with its kind, id and orbit.
/// </summary>
public sealed class OrbitFaultIsolationTests : VaultTestBase
{
	/// <summary>A notation the parser refuses.</summary>
	private const string Unparseable = "d{";

	/// <summary>A number too long for an <c>int</c>, which the parser lets escape as an <see cref="OverflowException"/> (I-003).</summary>
	private const string Overflowing = "d{99999999999}";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private readonly LogCapture _log = new();

	private Task<T> Declaratives<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<int> HardenNowAsync()
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(DateTimeOffset.Now, Ct));

	/// <summary>Routes the host's logs into <see cref="_log"/>; the test host clears every provider.</summary>
	private void CaptureLogs() => Vault.GetSingleton<ILoggerFactory>().AddProvider(_log);

	/// <summary>
	/// Stores a faulty orbit on an incentive past validation: its <c>Orbit</c> column and the notation of its schedule state,
	/// as a notation the engine has since narrowed would sit; or, with <paramref name="dropState"/>, no state at all, so the
	/// next resolution builds one lazily from the column.
	/// </summary>
	private Task CorruptIncentiveOrbitAsync(string incentiveId, string orbit, bool dropState = false) => Vault.QueryAsync(async context =>
	{
		await context.Database.ExecuteSqlAsync($"UPDATE Incentives SET Orbit = {orbit} WHERE Id = {incentiveId}", Ct);
		return dropState
			? await context.Database.ExecuteSqlAsync($"DELETE FROM OrbitScheduleStates WHERE IncentiveId = {incentiveId}", Ct)
			: await context.Database.ExecuteSqlAsync($"UPDATE OrbitScheduleStates SET StateJson = json_set(StateJson, '$.notation', {orbit}) WHERE IncentiveId = {incentiveId}", Ct);
	});

	private Task CorruptTimeframeOrbitAsync(long timeframeId, string orbit) => Vault.QueryAsync(async context =>
	{
		await context.Database.ExecuteSqlAsync($"UPDATE Timeframes SET Orbit = {orbit} WHERE Id = {timeframeId}", Ct);
		return await context.Database.ExecuteSqlAsync($"UPDATE OrbitScheduleStates SET StateJson = json_set(StateJson, '$.notation', {orbit}) WHERE TimeframeId = {timeframeId}", Ct);
	});

	private Task<string?> IncentiveStateAsync(string incentiveId)
		=> Vault.QueryAsync(context => context.IncentiveOrbitScheduleStates.AsNoTracking()
			.Where(state => state.IncentiveId == incentiveId)
			.Select(state => state.StateJson)
			.SingleOrDefaultAsync(Ct));

	private void AssertLoggedOnce(string ownerKind, object ownerId, string orbit, string pass)
		=> Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Warning
			&& entry.Exception is not null
			&& PlaintorchOrbitService.IsOrbitFault(entry.Exception)
			&& entry.Message == $"{ownerKind} {ownerId} has an orbit '{orbit}' that no longer resolves; {pass} skips it.");

	[Theory]
	[InlineData(Unparseable)]
	[InlineData(Overflowing)]
	[InlineData("d>2026/2/30")]
	public void A_notation_that_cannot_build_raises_an_orbit_fault(string orbit)
	{
		var exception = Record.Exception(() => OrbitDays.CreateState(orbit, new DateOnly(2026, 1, 1), OrbitDays.Gregorian));

		Assert.NotNull(exception);
		Assert.True(PlaintorchOrbitService.IsOrbitFault(exception));
	}

	[Fact]
	public void A_null_argument_is_never_taken_for_an_orbit_fault()
	{
		Assert.False(PlaintorchOrbitService.IsOrbitFault(new ArgumentNullException("incentive")));
		Assert.False(PlaintorchOrbitService.IsOrbitFault(new InvalidOperationException()));
	}

	[Theory]
	[InlineData(Unparseable, false)]
	[InlineData(Overflowing, false)]
	[InlineData(Unparseable, true)]
	public async Task The_rolling_pass_hardens_every_other_owner_past_a_faulty_orbit(string orbit, bool dropState)
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var faultyFate = await Declaratives(api => api.CreateFateAsync(new FatePlan("Faulty fate", Orbit: "d", EventDuration: 30), Ct));
		var faultyDecree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Faulty decree", Orbit: "d", DefaultLength: 5), Ct));
		var fate = await Declaratives(api => api.CreateFateAsync(new FatePlan("Sound fate", Orbit: "d", EventDuration: 30), Ct));
		var decree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Sound decree", Orbit: "d", DefaultLength: 5), Ct));
		await CorruptIncentiveOrbitAsync(faultyFate.Id, orbit, dropState);
		await CorruptIncentiveOrbitAsync(faultyDecree.Id, orbit, dropState);
		var faultyFateState = await IncentiveStateAsync(faultyFate.Id);
		var faultyDecreeState = await IncentiveStateAsync(faultyDecree.Id);
		CaptureLogs();

		await HardenNowAsync();

		var midnight = today.ToDateTime(TimeOnly.MinValue);
		Assert.Equal(1, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fate.Id && item.RecurrenceId == midnight, Ct)));
		Assert.Equal(1, await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id && item.RecurrenceId == midnight, Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == faultyFate.Id, Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == faultyDecree.Id, Ct)));

		// The faulty owners keep their cursor (or still have none) and cache no next occurrence; the sound ones roll on.
		Assert.Equal(faultyFateState, await IncentiveStateAsync(faultyFate.Id));
		Assert.Equal(faultyDecreeState, await IncentiveStateAsync(faultyDecree.Id));
		var next = await Vault.QueryAsync(context => context.Set<Declarative>().AsNoTracking().IgnoreAutoIncludes()
			.ToDictionaryAsync(item => item.Id, item => item.NextOccurrence, Ct));
		Assert.Null(next[faultyFate.Id]);
		Assert.Null(next[faultyDecree.Id]);
		Assert.NotNull(next[fate.Id]);
		Assert.NotNull(next[decree.Id]);

		AssertLoggedOnce(nameof(Fate), faultyFate.Id, orbit, "the rolling harden-on-time pass");
		AssertLoggedOnce(nameof(Decree), faultyDecree.Id, orbit, "the rolling harden-on-time pass");
	}

	[Fact]
	public async Task The_agenda_lists_every_other_owner_past_a_faulty_orbit()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var faultyFate = await Declaratives(api => api.CreateFateAsync(new FatePlan("Faulty fate", Orbit: "d", EventDuration: 30), Ct));
		var faultyDecree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Faulty decree", Orbit: "d", DefaultLength: 5), Ct));
		var fate = await Declaratives(api => api.CreateFateAsync(new FatePlan("Sound fate", Orbit: "d", EventDuration: 30), Ct));
		var decree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Sound decree", Orbit: "d", DefaultLength: 5), Ct));
		await CorruptIncentiveOrbitAsync(faultyFate.Id, Unparseable);
		await CorruptIncentiveOrbitAsync(faultyDecree.Id, Overflowing);
		CaptureLogs();

		var agenda = await Polaris(api => api.GetAgendaAsync(Ct));

		Assert.Contains(agenda.Eventives, item => item.FateId == fate.Id && item.Epoch.Date == today);
		Assert.Contains(agenda.Attentives, item => item.DecreeId == decree.Id && item.Epoch.Date == today);
		Assert.DoesNotContain(agenda.Eventives, item => item.FateId == faultyFate.Id);
		Assert.DoesNotContain(agenda.Attentives, item => item.DecreeId == faultyDecree.Id);
		AssertLoggedOnce(nameof(Fate), faultyFate.Id, Unparseable, "the agenda projection");
		AssertLoggedOnce(nameof(Decree), faultyDecree.Id, Overflowing, "the agenda projection");
	}

	[Fact]
	public async Task A_cycle_begin_completes_and_reflects_every_other_decree_past_a_faulty_orbit()
	{
		var lunar = await Directives(api => api.CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var faulty = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Faulty reflection", DirectiveId: lunar.Id, Orbit: "d", Reflect: true), Ct));
		var sound = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Evening reflection", DirectiveId: lunar.Id, Orbit: "d", Reflect: true), Ct));
		await CorruptIncentiveOrbitAsync(faulty.Id, Unparseable);
		var faultyState = await IncentiveStateAsync(faulty.Id);
		CaptureLogs();

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		Assert.NotNull(cycle.StartTime);
		Assert.Null(cycle.EndTime);
		var reflectives = await Vault.QueryAsync(context => context.Set<Reflective>().AsNoTracking()
			.Where(item => item.PolarisCycleId == cycle.Id)
			.ToListAsync(Ct));
		Assert.Single(reflectives, item => item.DecreeId == sound.Id);
		Assert.DoesNotContain(reflectives, item => item.DecreeId == faulty.Id);
		Assert.Equal(faultyState, await IncentiveStateAsync(faulty.Id));
		// The begin ran to its end: written and audited after materialising.
		Assert.Equal(1, await Vault.QueryAsync(context => context.AuditLogEntries.CountAsync(entry => entry.Action == "polaris.begin" && entry.SubjectId == cycle.Id, Ct)));
		AssertLoggedOnce(nameof(Decree), faulty.Id, Unparseable, "cycle-begin reflection");
	}

	[Fact]
	public async Task The_timeframe_candidates_hold_every_other_timeframe_past_a_faulty_orbit()
	{
		var lunar = await Directives(api => api.CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var faulty = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Faulty", new TimeOnly(0, 0), new TimeOnly(23, 59), Orbit: "d"), Ct));
		var sound = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Sound", new TimeOnly(0, 0), new TimeOnly(23, 59), Orbit: "d"), Ct));
		await CorruptTimeframeOrbitAsync(faulty.Id, Overflowing);
		CaptureLogs();
		var cycle = await Polaris(api => api.StartNewAsync(new DateTimeOffset(DateTime.Today.AddMinutes(5)), cancellationToken: Ct));

		var candidates = await Vault.WithScopeAsync(services => services.GetRequiredService<TimeframeCandidateService>().GetCandidateIdsAsync(cycle, Ct));

		Assert.Contains(sound.Id, candidates);
		Assert.DoesNotContain(faulty.Id, candidates);
		Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Warning
			&& entry.Exception is OverflowException
			&& entry.Message.StartsWith($"{nameof(Timeframe)} {faulty.Id} has an orbit '{Overflowing}' that no longer resolves;", StringComparison.Ordinal));
	}

	[Fact]
	public async Task A_save_that_touches_a_faulty_orbit_caches_no_next_occurrence_and_goes_through()
	{
		var fate = await Declaratives(api => api.CreateFateAsync(new FatePlan("Faulty fate", Orbit: "d", EventDuration: 30), Ct));
		await CorruptIncentiveOrbitAsync(fate.Id, Unparseable);
		CaptureLogs();

		var renamed = await Declaratives(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Title: "Renamed fate"), Ct));

		Assert.Equal("Renamed fate", renamed.Title);
		Assert.Null(await Vault.QueryAsync(context => context.Fates.AsNoTracking().IgnoreAutoIncludes()
			.Where(item => item.Id == fate.Id)
			.Select(item => item.NextOccurrence)
			.SingleAsync(Ct)));
		AssertLoggedOnce(nameof(Fate), fate.Id, Unparseable, "next-occurrence caching");
	}

	/// <summary>Collects every log entry the host writes, formatted, with its level and exception.</summary>
	private sealed class LogCapture : ILoggerProvider
	{
		private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

		public IReadOnlyCollection<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries.ToArray();

		public ILogger CreateLogger(string categoryName) => new Logger(_entries);

		public void Dispose()
		{
		}

		private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> entries) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
				=> entries.Enqueue((logLevel, formatter(state, exception), exception));
		}
	}
}
