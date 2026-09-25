using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Creation-time affinity is tri-state on both create paths — planning an executive and adding a decree (PEP100 patch 2):
/// an omitted affinity is Auto (the directive availability, else the college), an explicit <see langword="null"/> is no
/// affinity even when auto-inclusion would match, and an explicit id is used as given once it is known to exist (an
/// unknown one is refused before anything is created). A one-shot executive has no incentive, so Auto gives it none. The
/// returned executive carries the resolved timeframe navigation, not only its id.
/// </summary>
public sealed class CreationAffinityTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<T> Declaratives<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	private async Task<(long Availability, long College, long Manual)> CreateTimeframesAsync()
	{
		var owner = await Directives(api => api.CreateLunarAsync("Office Law", cancellationToken: Ct));
		var college = await Directives(api => api.CreateTimeframeAsync(owner.Id, new TimeframePlan(
			"Lab", new TimeOnly(8, 0), new TimeOnly(10, 0),
			AutoInclusion: TimeframeInclusion.College,
			AutoInclusionColleges: Enum.GetValues<ObjectiveCollege>()), Ct));
		var availability = await Directives(api => api.CreateTimeframeAsync(owner.Id, new TimeframePlan(
			"Office Hours", new TimeOnly(9, 0), new TimeOnly(17, 0),
			AutoInclusion: TimeframeInclusion.Availability), Ct));
		var manual = await Directives(api => api.CreateTimeframeAsync(owner.Id, new TimeframePlan(
			"Evening", new TimeOnly(18, 0), new TimeOnly(20, 0)), Ct));
		return (availability.Id, college.Id, manual.Id);
	}

	private async Task<StellarDirective> CreateAvailableDirectiveAsync(long availability)
	{
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, availability, Ct));
		return directive;
	}

	[Fact]
	public async Task An_omitted_affinity_is_auto_when_planning()
	{
		var (availability, college, _) = await CreateTimeframesAsync();
		var directive = await CreateAvailableDirectiveAsync(availability);

		var byCollege = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.Standalone, Title: "Loose end", College: ObjectiveCollege.Lore), cancellationToken: Ct));
		var byAvailability = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: directive.Id), cancellationToken: Ct));

		Assert.Equal(college, byCollege.Executive.AffinityTimeframeId);
		Assert.Equal(availability, byAvailability.Executive.AffinityTimeframeId);
		// The response carries the resolved navigation, not only the id.
		Assert.Equal("Lab", byCollege.Executive.AffinityTimeframe?.Title);
		Assert.Equal("Office Hours", byAvailability.Executive.AffinityTimeframe?.Title);
	}

	[Fact]
	public async Task An_omitted_affinity_is_auto_when_adding_a_decree()
	{
		var (availability, college, _) = await CreateTimeframesAsync();
		var directive = await CreateAvailableDirectiveAsync(availability);
		var loose = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Hydrate"), Ct));
		var bound = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Stretch", DirectiveId: directive.Id), Ct));

		var byCollege = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(loose.Id), cancellationToken: Ct));
		var byAvailability = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(bound.Id), cancellationToken: Ct));

		Assert.Equal(college, byCollege.AffinityTimeframeId);
		Assert.Equal(availability, byAvailability.AffinityTimeframeId);
		Assert.Equal("Lab", byCollege.AffinityTimeframe?.Title);
		Assert.Equal("Office Hours", byAvailability.AffinityTimeframe?.Title);
	}

	[Fact]
	public async Task An_explicit_null_is_no_affinity_even_when_auto_would_match()
	{
		var (availability, _, _) = await CreateTimeframesAsync();
		var directive = await CreateAvailableDirectiveAsync(availability);
		var decree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Stretch", DirectiveId: directive.Id), Ct));

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: directive.Id, AffinityTimeframeId: (long?)null), cancellationToken: Ct));
		var added = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id, AffinityTimeframeId: (long?)null), cancellationToken: Ct));

		Assert.Null(planned.Executive.AffinityTimeframeId);
		Assert.Null(planned.Executive.AffinityTimeframe);
		Assert.Null(added.AffinityTimeframeId);
		Assert.Null(added.AffinityTimeframe);
	}

	[Fact]
	public async Task An_explicit_id_is_used_as_given_on_both_paths()
	{
		var (availability, _, manual) = await CreateTimeframesAsync();
		var directive = await CreateAvailableDirectiveAsync(availability);
		var decree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Stretch", DirectiveId: directive.Id), Ct));

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: directive.Id, AffinityTimeframeId: manual), cancellationToken: Ct));
		var added = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id, AffinityTimeframeId: manual), cancellationToken: Ct));

		Assert.Equal(manual, planned.Executive.AffinityTimeframeId);
		Assert.Equal("Evening", planned.Executive.AffinityTimeframe?.Title);
		Assert.Equal(manual, added.AffinityTimeframeId);
		Assert.Equal("Evening", added.AffinityTimeframe?.Title);
	}

	[Fact]
	public async Task An_explicit_unknown_id_is_refused_on_both_paths_before_anything_is_created()
	{
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var decree = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Stretch"), Ct));

		var planError = await Assert.ThrowsAsync<InvalidOperationException>(() => Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: directive.Id, AffinityTimeframeId: 999_999L), cancellationToken: Ct)));
		var addError = await Assert.ThrowsAsync<InvalidOperationException>(() => Polaris(api => api.AddDecreeExecutiveAsync(
			new PolarisDecreeAdd(decree.Id, AffinityTimeframeId: 999_999L), cancellationToken: Ct)));

		Assert.Equal("Timeframe '999999' was not found.", planError.Message);
		Assert.Equal("Timeframe '999999' was not found.", addError.Message);
		// The refusal came first: no cycle was started, and no objective or executive was created.
		Assert.Equal(0, await Vault.QueryAsync(context => context.PolarisCycles.CountAsync(Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Objectives.CountAsync(item => item.Title == "Draft", Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Set<Executive>().CountAsync(Ct)));
	}

	[Fact]
	public async Task A_one_shot_executive_under_auto_gets_no_affinity()
	{
		await CreateTimeframesAsync();

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.OneShot, ExecutiveTitle: "Focus block"), cancellationToken: Ct));

		Assert.Null(planned.Objective);
		Assert.Null(planned.Executive.AffinityTimeframeId);
		Assert.Null(planned.Executive.AffinityTimeframe);
	}
}
