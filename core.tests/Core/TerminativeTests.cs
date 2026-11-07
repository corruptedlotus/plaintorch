using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The due contract and its terminatives (PEP103 patch 7, user ruling). Every item that carries a due — an objective, a
/// stellar directive, a checkpoint — spawns one terminative: projected while its due acts, hardened by time or by
/// interaction, and kept its origin's mirror by the state engine even once hardened. Its resolution is the engine's, read
/// from the origin's lifecycle; the ignore flag is the one thing a user sets.
/// </summary>
public sealed class TerminativeTests : VaultTestBase
{
	private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task<AgendaProjection> Project(DateOnly start, DateOnly end) => Vault.WithScopeAsync(services => services.GetRequiredService<AgendaProjectionService>().ProjectAsync(start, end, Ct));
	private Task<PolarisAgenda> Agenda() => Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));
	private Task<int> HardenAt(DateTimeOffset now) => Vault.WithScopeAsync(services => services.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(now, Ct));

	private static DateTimeOffset TodayAt(int hour) => new(Today.ToDateTime(new TimeOnly(hour, 0)));

	private Task<List<Terminative>> StoredAsync()
		=> Vault.QueryAsync(context => context.Terminatives.AsNoTracking().OrderBy(item => item.Id).ToListAsync(Ct));

	private async Task<Objective> ObjectiveDueAsync(string title, Due due)
	{
		var objective = await Objective(api => api.CreateStandaloneAsync(title, cancellationToken: Ct));
		return await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: due), Ct));
	}

	private async Task<StellarDirective> DirectiveDueAsync(string title, DateOnly due)
	{
		var directive = await Directive(api => api.CreateStandaloneAsync(title, cancellationToken: Ct));
		return await Directive(api => api.UpdateStellarAsync(directive.Id, new StellarDirectiveUpdate(Due: due), Ct));
	}

	private async Task<Checkpoint> CheckpointDueAsync(string title, Due due)
	{
		var checkpoint = await Deps(api => api.CreateCheckpointAsync(title, externalCondition: false, cancellationToken: Ct));
		return await Deps(api => api.UpdateCheckpointAsync(checkpoint.Id, new CheckpointUpdate(Due: due), Ct));
	}

	[Fact]
	public async Task Every_due_projects_its_terminative_and_nothing_is_stored()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today));
		var directive = await DirectiveDueAsync("Campaign", Today);
		var checkpoint = await CheckpointDueAsync("Gate", Due.At(Today.ToDateTime(new TimeOnly(18, 30))));

		var projection = await Project(Today, Today);

		Assert.Empty(projection.Eventives);
		Assert.Equal(3, projection.Terminatives.Count);
		Assert.All(projection.Terminatives, item => Assert.Equal(TerminativeResolution.Pending, item.Resolution));
		var ofObjective = Assert.Single(projection.Terminatives, item => item.ObjectiveId == objective.Id);
		Assert.Equal("Deliver", ofObjective.Objective?.Title);
		Assert.Equal(OrbitUnit.Day, ofObjective.Epoch.Granularity);
		var ofDirective = Assert.Single(projection.Terminatives, item => item.DirectiveId == directive.Id);
		Assert.Equal("Campaign", ofDirective.Directive?.Title);
		Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), ofDirective.RecurrenceId);
		var ofCheckpoint = Assert.Single(projection.Terminatives, item => item.CheckpointId == checkpoint.Id);
		Assert.Equal(OrbitUnit.Minute, ofCheckpoint.Epoch.Granularity);
		Assert.Equal(Today.ToDateTime(new TimeOnly(18, 30)), ofCheckpoint.Epoch.Moment);

		Assert.Empty(await StoredAsync());
	}

	[Fact]
	public async Task Time_hardens_each_due_once()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today.AddDays(-2)));
		var directive = await DirectiveDueAsync("Campaign", Today);
		await DirectiveDueAsync("Later campaign", Today.AddDays(1));

		Assert.Equal(2, await HardenAt(TodayAt(23)));
		Assert.Equal(0, await HardenAt(TodayAt(23)));

		var stored = await StoredAsync();
		Assert.Equal(2, stored.Count);
		Assert.Contains(stored, item => item.ObjectiveId == objective.Id && item.RecurrenceId == Today.AddDays(-2).ToDateTime(TimeOnly.MinValue));
		Assert.Contains(stored, item => item.DirectiveId == directive.Id);
		Assert.All(stored, item => Assert.Equal(TerminativeResolution.Pending, item.Resolution));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(Ct)));
	}

	[Fact]
	public async Task A_hardened_terminative_follows_its_due_and_goes_with_it()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today));
		var directive = await DirectiveDueAsync("Campaign", Today);
		await HardenAt(TodayAt(23));

		var moved = Today.AddDays(3).ToDateTime(new TimeOnly(14, 30));
		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: Due.At(moved, "Europe/Berlin")), Ct));
		await Directive(api => api.UpdateStellarAsync(directive.Id, new StellarDirectiveUpdate(Due: Today.AddDays(5)), Ct));

		var stored = await StoredAsync();
		var ofObjective = Assert.Single(stored, item => item.ObjectiveId == objective.Id);
		Assert.Equal(moved, ofObjective.Epoch.Moment);
		Assert.Equal(moved, ofObjective.RecurrenceId);
		Assert.Equal(OrbitUnit.Minute, ofObjective.Epoch.Granularity);
		Assert.Equal("Europe/Berlin", ofObjective.Epoch.TimeZone);
		var ofDirective = Assert.Single(stored, item => item.DirectiveId == directive.Id);
		Assert.Equal(Today.AddDays(5), ofDirective.Epoch.Date);

		// The moved due is not projected a second time beside its hardened terminative.
		Assert.Single((await Project(Today, Today.AddDays(7))).Terminatives, item => item.ObjectiveId == objective.Id);

		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: null), Ct));
		Assert.DoesNotContain(await StoredAsync(), item => item.ObjectiveId == objective.Id);
	}

	[Fact]
	public async Task The_resolution_follows_the_origins_lifecycle()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today));
		var directive = await DirectiveDueAsync("Campaign", Today);
		await HardenAt(TodayAt(23));

		async Task<TerminativeResolution> ResolutionOf(Func<Terminative, bool> match) => (await StoredAsync()).Single(match).Resolution;

		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		Assert.Equal(TerminativeResolution.Resolved, await ResolutionOf(item => item.ObjectiveId == objective.Id));

		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Standby), Ct));
		Assert.Equal(TerminativeResolution.Pending, await ResolutionOf(item => item.ObjectiveId == objective.Id));

		// Set aside resolves it too: its due acts no more.
		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Failed), Ct));
		Assert.Equal(TerminativeResolution.Resolved, await ResolutionOf(item => item.ObjectiveId == objective.Id));

		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		Assert.Equal(TerminativeResolution.Resolved, await ResolutionOf(item => item.DirectiveId == directive.Id));

		// A finished or set-aside origin projects no terminative of its own: only the hardened one shows.
		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: Due.On(Today.AddDays(1))), Ct));
		var projected = (await Project(Today, Today.AddDays(2))).Terminatives;
		Assert.Equal(TerminativeResolution.Resolved, Assert.Single(projected, item => item.ObjectiveId == objective.Id).Resolution);
	}

	[Fact]
	public async Task A_checkpoints_due_acts_until_it_unlocks()
	{
		var checkpoint = await CheckpointDueAsync("Permit", Due.On(Today.AddDays(-1)));

		// Its due has passed while it is still locked, which sets it aside; its terminative still acts.
		Assert.Single((await Project(Today.AddDays(-1), Today)).Terminatives, item => item.CheckpointId == checkpoint.Id);
		Assert.Equal(1, await HardenAt(TodayAt(23)));
		Assert.Equal(TerminativeResolution.Pending, Assert.Single(await StoredAsync()).Resolution);
		Assert.Contains((await Agenda()).Terminatives, item => item.CheckpointId == checkpoint.Id);

		await Deps(api => api.SetExternalConditionAsync(checkpoint.Id, true, Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);
		Assert.Equal(TerminativeResolution.Resolved, Assert.Single(await StoredAsync()).Resolution);
		Assert.DoesNotContain((await Agenda()).Terminatives, item => item.CheckpointId == checkpoint.Id);
	}

	[Fact]
	public async Task The_agenda_lists_pending_dues_up_to_its_horizon_overdue_ones_included()
	{
		var overdue = await ObjectiveDueAsync("Overdue", Due.On(Today.AddDays(-3)));
		var soon = await ObjectiveDueAsync("Soon", Due.On(Today.AddDays(2)));
		var far = await ObjectiveDueAsync("Far", Due.On(Today.AddDays(30)));
		var done = await ObjectiveDueAsync("Done", Due.On(Today.AddDays(1)));
		await Objective(api => api.ShiftWorkflowAsync(done.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));

		var listed = (await Agenda()).Terminatives.Select(item => item.ObjectiveId).ToList();

		Assert.Equal([overdue.Id, soon.Id], listed);
		Assert.DoesNotContain(far.Id, listed);
	}

	[Fact]
	public async Task The_ignore_flag_is_the_one_thing_a_user_sets()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today.AddDays(1)));

		// Raising the flag on a projected terminative hardens it, and the agenda stops listing it.
		var ignored = await Declarative(api => api.UpdateTerminativeAsync(new TerminativeOccurrenceRef(OriginId: objective.Id), new TerminativeUpdate(Ignored: true), Ct));
		Assert.True(ignored.Ignored);
		Assert.NotEqual(0, ignored.Id);
		Assert.Equal(TerminativeResolution.Pending, ignored.Resolution);
		Assert.Equal("Deliver", ignored.Objective?.Title);
		Assert.DoesNotContain((await Agenda()).Terminatives, item => item.ObjectiveId == objective.Id);

		// The engine never touches it: the origin moving on keeps the flag up.
		await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: Due.On(Today.AddDays(2))), Ct));
		Assert.True(Assert.Single(await StoredAsync()).Ignored);

		var lowered = await Declarative(api => api.UpdateTerminativeAsync(new TerminativeOccurrenceRef(Id: ignored.Id), new TerminativeUpdate(Ignored: false), Ct));
		Assert.False(lowered.Ignored);
		Assert.Contains((await Agenda()).Terminatives, item => item.ObjectiveId == objective.Id);

		// Whatever a writer stores as its resolution, the engine reads it again from the origin.
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<Pleiades.Vault.Database.PlainfraContext>();
			var row = await context.Terminatives.SingleAsync(Ct);
			row.Resolution = TerminativeResolution.Resolved;
			await context.SaveChangesAsync(Ct);
		});
		Assert.Equal(TerminativeResolution.Pending, Assert.Single(await StoredAsync()).Resolution);
	}

	[Fact]
	public async Task A_due_that_does_not_act_hardens_no_terminative_on_interaction()
	{
		var undated = await Objective(api => api.CreateStandaloneAsync("Undated", cancellationToken: Ct));
		var done = await ObjectiveDueAsync("Done", Due.On(Today));
		await Objective(api => api.ShiftWorkflowAsync(done.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Declarative(api => api.UpdateTerminativeAsync(new TerminativeOccurrenceRef(OriginId: undated.Id), new TerminativeUpdate(Ignored: true), Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Declarative(api => api.UpdateTerminativeAsync(new TerminativeOccurrenceRef(OriginId: done.Id), new TerminativeUpdate(Ignored: true), Ct)));
		Assert.Empty(await StoredAsync());
	}

	[Fact]
	public async Task Deleting_an_origin_deletes_its_terminative()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today));
		var checkpoint = await CheckpointDueAsync("Gate", Due.On(Today));
		await HardenAt(TodayAt(23));
		Assert.Equal(2, (await StoredAsync()).Count);

		await Objective(async api =>
		{
			await api.DeleteAsync(objective.Id, Ct);
			return true;
		});
		await Deps(async api =>
		{
			await api.DeleteCheckpointAsync(checkpoint.Id, Ct);
			return true;
		});

		Assert.Empty(await StoredAsync());
	}

	[Fact]
	public async Task An_eventive_edge_names_a_fate_and_never_an_objective()
	{
		var objective = await ObjectiveDueAsync("Deliver", Due.On(Today));
		var other = await Objective(api => api.CreateStandaloneAsync("Follow-up", cancellationToken: Ct));

		var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(
			new EndpointRef(DependencyEndpointKind.Eventive, objective.Id, Today.ToDateTime(TimeOnly.MinValue)),
			new EndpointRef(DependencyEndpointKind.Objective, other.Id),
			cancellationToken: Ct)));
		Assert.Contains("expected a fate", refused.Message, StringComparison.Ordinal);
	}
}
