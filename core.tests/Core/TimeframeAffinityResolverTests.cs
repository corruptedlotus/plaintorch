using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The one auto-affinity resolver (PEP100 patch, availability per PEP100 patch 2). College auto-inclusion considers only
/// College-mode timeframes and lets the lowest id win. The combined resolver puts a directive availability first: the
/// nearest directive — the incentive's own directive, then its ancestors — whose availability names an Availability-mode
/// timeframe wins over a farther one and over any matching college timeframe; a stale availability pointing at a
/// timeframe in another mode is ignored, and a looping lineage (the vault is hand-editable) ends the walk rather than
/// hanging it.
/// </summary>
public sealed class TimeframeAffinityResolverTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<long?> ResolveForCollegeAsync(ObjectiveCollege college)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<TimeframeAffinityResolver>().ResolveForCollegeAsync(college, Ct));

	private Task<long?> ResolveAsync(string? directiveId, ObjectiveCollege college)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<TimeframeAffinityResolver>().ResolveAsync(directiveId, college, Ct));

	private async Task<long> CreateTimeframeAsync(string title, TimeframeInclusion inclusion, params ObjectiveCollege[] colleges)
	{
		var lunar = await Directives(api => api.CreateLunarAsync($"{title} Law", cancellationToken: Ct));
		var timeframe = await Directives(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan(title, new TimeOnly(9, 0), new TimeOnly(17, 0), AutoInclusion: inclusion, AutoInclusionColleges: colleges),
			Ct));
		return timeframe.Id;
	}

	[Fact]
	public async Task The_college_resolver_matches_only_the_college_it_lists()
	{
		var lab = await CreateTimeframeAsync("Lab", TimeframeInclusion.College, ObjectiveCollege.Creation);

		Assert.Equal(lab, await ResolveForCollegeAsync(ObjectiveCollege.Creation));
		Assert.Null(await ResolveForCollegeAsync(ObjectiveCollege.Lore));
	}

	[Fact]
	public async Task The_lowest_id_college_timeframe_wins()
	{
		var first = await CreateTimeframeAsync("First", TimeframeInclusion.College, ObjectiveCollege.Creation);
		await CreateTimeframeAsync("Second", TimeframeInclusion.College, ObjectiveCollege.Lore, ObjectiveCollege.Creation);

		Assert.Equal(first, await ResolveForCollegeAsync(ObjectiveCollege.Creation));
	}

	[Fact]
	public async Task The_college_resolver_never_returns_availability_or_none_timeframes()
	{
		var availability = await CreateTimeframeAsync("Office", TimeframeInclusion.Availability);
		var none = await CreateTimeframeAsync("Dawn", TimeframeInclusion.None);
		// A college list sent without a mode lands on the timeframe even outside College mode; it still never matches.
		await Directives(api => api.UpdateTimeframeAsync(availability, new TimeframeUpdate(AutoInclusionColleges: [ObjectiveCollege.Creation]), Ct));
		await Directives(api => api.UpdateTimeframeAsync(none, new TimeframeUpdate(AutoInclusionColleges: [ObjectiveCollege.Creation]), Ct));

		Assert.Null(await ResolveForCollegeAsync(ObjectiveCollege.Creation));
		Assert.Null(await ResolveAsync(null, ObjectiveCollege.Creation));
	}

	[Fact]
	public async Task A_directive_availability_beats_a_matching_college_timeframe()
	{
		var lab = await CreateTimeframeAsync("Lab", TimeframeInclusion.College, ObjectiveCollege.Creation);
		var office = await CreateTimeframeAsync("Office", TimeframeInclusion.Availability);
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));

		// Without an availability the college decides; the directive's own availability then takes precedence.
		Assert.Equal(lab, await ResolveAsync(directive.Id, ObjectiveCollege.Creation));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, office, Ct));
		Assert.Equal(office, await ResolveAsync(directive.Id, ObjectiveCollege.Creation));

		// No directive at all: only the college can match.
		Assert.Equal(lab, await ResolveAsync(null, ObjectiveCollege.Creation));
	}

	[Fact]
	public async Task A_descendant_inherits_the_nearest_ancestor_availability()
	{
		var far = await CreateTimeframeAsync("Far", TimeframeInclusion.Availability);
		var near = await CreateTimeframeAsync("Near", TimeframeInclusion.Availability);
		var root = await Directives(api => api.CreateStandaloneAsync("Root", cancellationToken: Ct));
		var middle = await Directives(api => api.CreateFromParentAsync(root.Id, "Middle", cancellationToken: Ct));
		var leaf = await Directives(api => api.CreateFromParentAsync(middle.Id, "Leaf", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(root.Id, far, Ct));

		// The leaf and its parent inherit the root's availability.
		Assert.Equal(far, await ResolveAsync(leaf.Id, ObjectiveCollege.Unspecified));
		Assert.Equal(far, await ResolveAsync(middle.Id, ObjectiveCollege.Unspecified));

		// A nearer ancestor's availability wins over the root's; the root itself keeps its own.
		await Directives(api => api.SetAvailabilityAsync(middle.Id, near, Ct));
		Assert.Equal(near, await ResolveAsync(leaf.Id, ObjectiveCollege.Unspecified));
		Assert.Equal(far, await ResolveAsync(root.Id, ObjectiveCollege.Unspecified));
	}

	[Fact]
	public async Task An_availability_pointing_at_a_non_availability_timeframe_is_ignored()
	{
		var office = await CreateTimeframeAsync("Office", TimeframeInclusion.Availability);
		var dawn = await CreateTimeframeAsync("Dawn", TimeframeInclusion.None);
		var root = await Directives(api => api.CreateStandaloneAsync("Root", cancellationToken: Ct));
		var leaf = await Directives(api => api.CreateFromParentAsync(root.Id, "Leaf", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(root.Id, office, Ct));
		// Only a hand-edited database can hold this; the API refuses it and mode changes clear it.
		await Vault.QueryAsync(context => context.Directives.Where(item => item.Id == leaf.Id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AvailabilityTimeframeId, dawn), Ct));

		// The leaf's stale pick is skipped and the walk carries on to the root.
		Assert.Equal(office, await ResolveAsync(leaf.Id, ObjectiveCollege.Unspecified));
	}

	[Fact]
	public async Task A_cyclic_lineage_ends_the_walk_instead_of_hanging()
	{
		var lab = await CreateTimeframeAsync("Lab", TimeframeInclusion.College, ObjectiveCollege.Lore);
		var office = await CreateTimeframeAsync("Office", TimeframeInclusion.Availability);
		var first = await Directives(api => api.CreateStandaloneAsync("First", cancellationToken: Ct));
		var second = await Directives(api => api.CreateFromParentAsync(first.Id, "Second", cancellationToken: Ct));
		// A hand-edited vault can close the loop: First's parent is Second, whose parent is First.
		await Vault.QueryAsync(context => context.Directives.Where(item => item.Id == first.Id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ParentDirectiveId, second.Id), Ct));

		var resolution = ResolveAsync(second.Id, ObjectiveCollege.Lore);
		Assert.Same(resolution, await Task.WhenAny(resolution, Task.Delay(TimeSpan.FromSeconds(30), Ct)));
		Assert.Equal(lab, await resolution);

		// The loop is still walked in full: an availability anywhere on it is found.
		await Directives(api => api.SetAvailabilityAsync(first.Id, office, Ct));
		Assert.Equal(office, await ResolveAsync(second.Id, ObjectiveCollege.Lore));
	}
}
