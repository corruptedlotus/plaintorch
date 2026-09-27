using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Directive availability reaches every auto-affinity path (PEP100 patch 2): an objective executive planned from a
/// directive or from an existing objective, a decree executive, and a cycle-begin reflective of a lunar reflect-decree
/// are all affined to their directive's — or nearest ancestor's — availability, ahead of a college timeframe that also
/// matches. Without a directive availability the college still decides. Availability follows the directive lineage
/// only — never a parent incentive — seeds at creation only (a later pick never rewrites an existing executive), and
/// ignores the owning lunar directive's status.
/// </summary>
public sealed class AvailabilityAffinityTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<T> Declaratives<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	private Task<T> Objectives<T>(Func<IObjectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));

	/// <summary>
	/// Creates an Availability timeframe and a College timeframe that matches every college, so any availability
	/// resolution that falls through to the college shows up as the college timeframe.
	/// </summary>
	private async Task<(long Availability, long College)> CreateTimeframesAsync()
	{
		var owner = await Directives(api => api.CreateLunarAsync("Office Law", cancellationToken: Ct));
		var college = await Directives(api => api.CreateTimeframeAsync(owner.Id, new TimeframePlan(
			"Lab", new TimeOnly(8, 0), new TimeOnly(10, 0),
			AutoInclusion: TimeframeInclusion.College,
			AutoInclusionColleges: Enum.GetValues<ObjectiveCollege>()), Ct));
		var availability = await Directives(api => api.CreateTimeframeAsync(owner.Id, new TimeframePlan(
			"Office Hours", new TimeOnly(9, 0), new TimeOnly(17, 0),
			AutoInclusion: TimeframeInclusion.Availability), Ct));
		return (availability.Id, college.Id);
	}

	[Fact]
	public async Task An_executive_planned_from_a_descendant_directive_takes_the_ancestor_availability()
	{
		var (availability, college) = await CreateTimeframesAsync();
		var root = await Directives(api => api.CreateStandaloneAsync("Root", cancellationToken: Ct));
		var child = await Directives(api => api.CreateFromParentAsync(root.Id, "Child", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(root.Id, availability, Ct));

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: child.Id, College: ObjectiveCollege.Creation), cancellationToken: Ct));
		Assert.Equal(availability, planned.Executive.AffinityTimeframeId);

		// A standalone objective has no directive, so only its college can match.
		var standalone = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.Standalone, Title: "Loose end", College: ObjectiveCollege.Creation), cancellationToken: Ct));
		Assert.Equal(college, standalone.Executive.AffinityTimeframeId);
	}

	[Fact]
	public async Task An_executive_planned_from_an_existing_objective_takes_its_directive_availability()
	{
		var (availability, _) = await CreateTimeframesAsync();
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, availability, Ct));
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(directive.Id, "Wire it", cancellationToken: Ct));

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), cancellationToken: Ct));

		Assert.Equal(availability, planned.Executive.AffinityTimeframeId);
	}

	[Fact]
	public async Task A_decree_executive_takes_its_directive_availability()
	{
		var (availability, college) = await CreateTimeframesAsync();
		var root = await Directives(api => api.CreateStandaloneAsync("Root", cancellationToken: Ct));
		var child = await Directives(api => api.CreateFromParentAsync(root.Id, "Child", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(root.Id, availability, Ct));
		var bound = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Stretch", DirectiveId: child.Id), Ct));
		var loose = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Hydrate"), Ct));

		var boundExecutive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(bound.Id), cancellationToken: Ct));
		var looseExecutive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(loose.Id), cancellationToken: Ct));

		Assert.Equal(availability, boundExecutive.AffinityTimeframeId);
		Assert.Equal(college, looseExecutive.AffinityTimeframeId);
	}

	[Fact]
	public async Task A_cycle_begin_reflective_takes_its_lunar_directive_availability()
	{
		var (availability, college) = await CreateTimeframesAsync();
		var root = await Directives(api => api.CreateLunarAsync("Tide", cancellationToken: Ct));
		var child = await Directives(api => api.CreateLunarAsync("Ebb", parentDirectiveId: root.Id, cancellationToken: Ct));
		var sibling = await Directives(api => api.CreateLunarAsync("Calm", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(root.Id, availability, Ct));
		var inherited = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Evening reflection", DirectiveId: child.Id, Orbit: "d", Reflect: true), Ct));
		var plain = await Declaratives(api => api.CreateDecreeAsync(new DecreePlan("Morning reflection", DirectiveId: sibling.Id, Orbit: "d", Reflect: true), Ct));

		await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		var reflectives = await Vault.QueryAsync(context => context.Set<Reflective>().AsNoTracking().ToListAsync(Ct));
		Assert.Equal(availability, reflectives.Single(item => item.DecreeId == inherited.Id).AffinityTimeframeId);
		Assert.Equal(college, reflectives.Single(item => item.DecreeId == plain.Id).AffinityTimeframeId);
	}

	[Fact]
	public async Task Availability_never_inherits_through_a_parent_incentive()
	{
		var (availability, college) = await CreateTimeframesAsync();
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, availability, Ct));
		var parent = await Objectives(api => api.CreateFromDirectiveAsync(directive.Id, "Ship it", cancellationToken: Ct));
		var subtask = await Objectives(api => api.CreateStandaloneAsync("Write the notes", cancellationToken: Ct));
		subtask = await Objectives(api => api.UpdateAsync(subtask.Id, new ObjectiveUpdate(ParentIncentiveId: parent.Id), Ct));
		Assert.Equal(parent.Id, subtask.ParentIncentiveId);
		Assert.Null(subtask.DirectiveId);

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromObjective, ObjectiveId: subtask.Id), cancellationToken: Ct));

		// Only the directive lineage carries availability; the subtask has no directive, so its college decides.
		Assert.Equal(college, planned.Executive.AffinityTimeframeId);
	}

	[Fact]
	public async Task Availability_seeds_at_creation_only_and_never_rewrites_an_existing_executive()
	{
		var (availability, college) = await CreateTimeframesAsync();
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: directive.Id, College: ObjectiveCollege.Creation), cancellationToken: Ct));
		Assert.Equal(college, planned.Executive.AffinityTimeframeId);

		await Directives(api => api.SetAvailabilityAsync(directive.Id, availability, Ct));

		var stored = await Vault.QueryAsync(context => context.Set<Executive>().AsNoTracking().SingleAsync(item => item.Id == planned.Executive.Id, Ct));
		Assert.Equal(college, stored.AffinityTimeframeId);

		// Only the next executive created under the directive takes the availability.
		var next = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Review", DirectiveId: directive.Id, College: ObjectiveCollege.Creation), cancellationToken: Ct));
		Assert.Equal(availability, next.Executive.AffinityTimeframeId);
	}

	[Theory]
	[InlineData(LunarDirectiveStatus.OnHold)]
	[InlineData(LunarDirectiveStatus.Stale)]
	public async Task An_availability_applies_whatever_its_owning_lunar_directive_status(LunarDirectiveStatus status)
	{
		var (availability, _) = await CreateTimeframesAsync();
		var owner = await Vault.QueryAsync(context => context.Timeframes.AsNoTracking().Where(item => item.Id == availability).Select(item => item.DirectiveId).SingleAsync(Ct));
		// Through Active first, so the owner really moves into the tested status (a new lunar directive starts on hold).
		await Directives(api => api.ShiftLunarWorkflowAsync(owner, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Active), Ct));
		var shifted = await Directives(api => api.ShiftLunarWorkflowAsync(owner, new LunarDirectiveWorkflowShift(status), Ct));
		Assert.Equal(status, shifted.Status);
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, availability, Ct));

		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(
			PolarisExecutivePlanningMode.FromDirective, Title: "Draft", DirectiveId: directive.Id, College: ObjectiveCollege.Creation), cancellationToken: Ct));

		Assert.Equal(availability, planned.Executive.AffinityTimeframeId);
	}
}
