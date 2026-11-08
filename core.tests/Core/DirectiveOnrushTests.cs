using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Stellar directives as onrush members (PEP102 patch 10): an onrush tracks them the way it tracks objectives — they
/// join and leave through their own endpoints, ride the placeholder's begin onto the real sprint, are freed (not
/// deleted) with the sprint, and name their sprint in frontmatter like an objective does.
/// </summary>
public sealed class DirectiveOnrushTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task Onrush(Func<IOnrushSprintApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));

	[Fact]
	public async Task A_stellar_directive_joins_and_leaves_an_onrush_and_a_lunar_one_cannot()
	{
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Sprint"), Ct));
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var lunar = await Directive(api => api.CreateLunarAsync("Moon Law", cancellationToken: Ct));

		var joined = await Directive(api => api.AddToOnrushAsync(directive.Id, sprint.Id, Ct));
		Assert.Equal(sprint.Id, joined.OnrushSprintId);
		// Its status is its own business: joining promotes nothing.
		Assert.Equal(DirectiveStatus.Planned, joined.Status);
		// The sprint serves it as a member, and the note names the sprint like an objective's does.
		var served = await Onrush(api => api.GetPlanningAsync(Ct));
		Assert.Equal(directive.Id, Assert.Single(served!.Directives).Id);
		Assert.Contains($"onrush: {sprint.Id}", Vault.ReadVaultFile("Directives/Campaign/Campaign.md"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Directive(api => api.AddToOnrushAsync(lunar.Id, sprint.Id, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Directive(api => api.AddToOnrushAsync(directive.Id, "x9999", Ct)));

		var left = await Directive(api => api.RemoveFromOnrushAsync(directive.Id, Ct));
		Assert.Null(left.OnrushSprintId);
		Assert.Empty((await Onrush(api => api.GetPlanningAsync(Ct)))!.Directives);
		Assert.DoesNotContain("onrush: x", Vault.ReadVaultFile("Directives/Campaign/Campaign.md"));
	}

	[Fact]
	public async Task A_stellar_directive_joining_a_slot_with_no_sprint_creates_it()
	{
		// The directive is the first thing to ask for either slot, so nothing has made a sprint for it to land in.
		var planned = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var front = await Directive(api => api.CreateFromParentAsync(planned.Id, "Front", cancellationToken: Ct));

		var inPlan = await Directive(api => api.AddToOnrushAsync(planned.Id, OnrushSlot.Planning, Ct));
		Assert.Equal(OnrushSprint.PlanningPlaceholderId, inPlan.OnrushSprintId);

		// The active slot begins the plan, so the first directive rides along into the sprint the second one joins.
		var active = await Directive(api => api.AddToOnrushAsync(front.Id, OnrushSlot.Active, Ct));
		Assert.NotEqual(OnrushSprint.PlanningPlaceholderId, active.OnrushSprintId);
		var current = await Onrush(api => api.GetAsync(cancellationToken: Ct));
		Assert.Equal(active.OnrushSprintId, current!.Id);
		Assert.Equal([front.Id, planned.Id], current.Directives.Select(directive => directive.Id).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task A_directive_member_rides_the_begin_onto_the_real_sprint_and_is_freed_by_the_delete()
	{
		var planning = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Sprint"), Ct));
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		await Directive(api => api.AddToOnrushAsync(directive.Id, planning.Id, Ct));

		// Begun, the placeholder becomes a dated sprint and its directive member comes along.
		var begun = await Onrush(api => api.BeginAsync(planning.Id, cancellationToken: Ct));
		Assert.NotEqual(OnrushSprint.PlanningPlaceholderId, begun.Id);
		var current = await Onrush(api => api.GetAsync(cancellationToken: Ct));
		Assert.Equal(directive.Id, Assert.Single(current!.Directives).Id);
		Assert.Equal(begun.Id, Assert.IsType<StellarDirective>(await Directive(api => api.GetAsync(directive.Id, Ct))).OnrushSprintId);

		// Deleted, the sprint frees the directive: it survives, detached, its note re-saved without the sprint.
		await Onrush(api => api.DeleteAsync(begun.Id, Ct));
		var freed = Assert.IsType<StellarDirective>(await Directive(api => api.GetAsync(directive.Id, Ct)));
		Assert.Null(freed.OnrushSprintId);
		Assert.DoesNotContain("onrush: x", Vault.ReadVaultFile("Directives/Campaign/Campaign.md"));
	}

	[Fact]
	public async Task Leaving_a_sprint_drops_the_onrush_key_from_an_objectives_note_too()
	{
		// The note writer used to skip a cleared field and then resurrect its old value from the preserved frontmatter,
		// so an objective that left its sprint kept naming it — and the next sync put it straight back.
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Sprint"), Ct));
		var objective = await Vault.SeedStandaloneObjectiveAsync("Quest");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().AddToOnrushAsync(objective.Id, sprint.Id, Ct));
		Assert.Contains($"onrush: {sprint.Id}", Vault.ReadVaultFile("Objectives/Quest.md"));

		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().RemoveFromOnrushAsync(objective.Id, Ct));

		Assert.DoesNotContain("onrush:", Vault.ReadVaultFile("Objectives/Quest.md"));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Objectives/Quest.md"));
		Assert.Null((await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes().FirstAsync(item => item.Id == objective.Id, Ct))).OnrushSprintId);
	}

	[Fact]
	public async Task A_note_naming_a_sprint_that_does_not_exist_is_normalized_by_the_watcher()
	{
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var note = "Directives/Campaign/Campaign.md";
		var content = Vault.ReadVaultFile(note);
		Vault.WriteVaultFile(note, content.Replace("---" + Environment.NewLine, "---" + Environment.NewLine + "onrush: x4242" + Environment.NewLine, StringComparison.Ordinal));

		await Vault.ReconcileAsync(Vault.AbsolutePath(note));

		Assert.Null((await Vault.QueryAsync(context => context.Directives.OfType<StellarDirective>().AsNoTracking().IgnoreAutoIncludes()
			.FirstAsync(item => item.Id == directive.Id, Ct))).OnrushSprintId);
	}
}
