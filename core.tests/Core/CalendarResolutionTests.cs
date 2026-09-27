using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The resolver calendar is chosen by a single vault-wide default preference (PEP116) when a declarative names
/// none of its own — no longer a fate-vs-decree kind split — while an explicit per-declarative calendar still wins.
/// </summary>
public sealed class CalendarResolutionTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<IOrbitCalendar> ResolveAsync(Incentive incentive)
		=> Vault.WithScopeAsync(services => Task.FromResult(services.GetRequiredService<PlaintorchOrbitService>().ResolveCalendar(incentive)));

	private Task SetDefaultCalendarAsync(DeclarativeCalendar calendar)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.DefaultCalendar, calendar, Ct));

	[Fact]
	public async Task The_default_calendar_preference_resolves_declaratives_without_their_own()
	{
		// Default (Pleiadean): a fate — which used to resolve Gregorian by kind — now follows the global default.
		Assert.Same(OrbitDays.Pleiadean, await ResolveAsync(new Fate { Id = "f1", Title = "Event" }));

		// Flip the global default to Gregorian: a decree — which used to resolve Pleiadean by kind — follows it too.
		await SetDefaultCalendarAsync(DeclarativeCalendar.Gregorian);
		Assert.Same(OrbitDays.Gregorian, await ResolveAsync(new Decree { Id = "r1", Title = "Routine" }));
	}

	[Fact]
	public async Task An_explicit_declarative_calendar_wins_over_the_default_preference()
	{
		await SetDefaultCalendarAsync(DeclarativeCalendar.Gregorian);
		Assert.Same(OrbitDays.Pleiadean, await ResolveAsync(new Fate { Id = "f2", Title = "Zoned", Calendar = DeclarativeCalendar.Pleiadean }));
	}
}
