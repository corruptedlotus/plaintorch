using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Every directive — stellar or lunar — can pick an Availability-mode timeframe as its availability (PEP100 patch 2).
/// Setting refuses an unknown timeframe or one in another inclusion mode, and <see langword="null"/> clears it. The
/// field is database-only, so a markdown sync of the directive's note must keep it, and it never outlives its
/// timeframe's role: leaving Availability mode, deleting the timeframe, or deleting the lunar directive that owns it
/// clears every directive pointing at it — tracked, so the change feed announces each cleared directive rather than the
/// database's SetNull key nulling the column silently. The clear rides the state-policy save hook, so it holds on every
/// pathway: the directive API, a bare unit of work, and the watcher replaying a deleted lunar directive note.
/// </summary>
public sealed class DirectiveAvailabilityTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private async Task<long> CreateTimeframeAsync(TimeframeInclusion inclusion, string title = "Office Hours")
	{
		var lunar = await Directives(api => api.CreateLunarAsync($"{title} Law", cancellationToken: Ct));
		var timeframe = await Directives(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan(title, new TimeOnly(9, 0), new TimeOnly(17, 0), AutoInclusion: inclusion),
			Ct));
		return timeframe.Id;
	}

	/// <summary>Collects what the change feed announces while <paramref name="action"/> runs.</summary>
	private async Task<IReadOnlyList<EntityChange>> RecordAsync(Func<Task> action)
	{
		using var subscription = Vault.GetSingleton<PlaintorchChangeBroker>().Subscribe();

		await action();

		var changes = new List<EntityChange>();
		while (subscription.Reader.TryRead(out var change))
		{
			changes.Add(change);
		}

		return changes;
	}

	private static void AssertAnnouncedModified(IReadOnlyList<EntityChange> changes, string type, string id)
		=> Assert.Contains(changes, change => change.Type == type && change.Id == id && change.Operation == EntityChangeOperation.Modified);

	private Task<long?> AvailabilityOfAsync(string directiveId)
		=> Vault.QueryAsync(context => context.Directives.AsNoTracking()
			.Where(directive => directive.Id == directiveId)
			.Select(directive => directive.AvailabilityTimeframeId)
			.SingleAsync(Ct));

	[Fact]
	public async Task A_stellar_or_lunar_directive_can_set_and_clear_an_availability()
	{
		var timeframeId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		var stellar = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var lunar = await Directives(api => api.CreateLunarAsync("Tide", cancellationToken: Ct));

		var setStellar = await Directives(api => api.SetAvailabilityAsync(stellar.Id, timeframeId, Ct));
		var setLunar = await Directives(api => api.SetAvailabilityAsync(lunar.Id, timeframeId, Ct));
		Assert.Equal(timeframeId, setStellar.AvailabilityTimeframeId);
		Assert.Equal(timeframeId, setLunar.AvailabilityTimeframeId);
		Assert.Equal(timeframeId, await AvailabilityOfAsync(stellar.Id));
		Assert.Equal(timeframeId, await AvailabilityOfAsync(lunar.Id));

		var cleared = await Directives(api => api.SetAvailabilityAsync(stellar.Id, null, Ct));
		Assert.Null(cleared.AvailabilityTimeframeId);
		Assert.Null(await AvailabilityOfAsync(stellar.Id));
		Assert.Equal(timeframeId, await AvailabilityOfAsync(lunar.Id));
	}

	[Fact]
	public async Task Setting_refuses_an_unknown_timeframe_a_non_availability_timeframe_and_an_unknown_directive()
	{
		var collegeId = await CreateTimeframeAsync(TimeframeInclusion.College, "Lab");
		var noneId = await CreateTimeframeAsync(TimeframeInclusion.None, "Dawn");
		var availabilityId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Directives(api => api.SetAvailabilityAsync(directive.Id, 999_999L, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Directives(api => api.SetAvailabilityAsync(directive.Id, collegeId, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Directives(api => api.SetAvailabilityAsync(directive.Id, noneId, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Directives(api => api.SetAvailabilityAsync("A999999", availabilityId, Ct)));

		// A refused request leaves the directive untouched.
		Assert.Null(await AvailabilityOfAsync(directive.Id));
	}

	[Fact]
	public async Task A_timeframe_leaving_availability_mode_clears_the_directives_pointing_at_it()
	{
		var timeframeId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		var otherId = await CreateTimeframeAsync(TimeframeInclusion.Availability, "Evenings");
		var first = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var second = await Directives(api => api.CreateLunarAsync("Tide", cancellationToken: Ct));
		var bystander = await Directives(api => api.CreateStandaloneAsync("Heron", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(first.Id, timeframeId, Ct));
		await Directives(api => api.SetAvailabilityAsync(second.Id, timeframeId, Ct));
		await Directives(api => api.SetAvailabilityAsync(bystander.Id, otherId, Ct));

		// Staying in Availability mode (a rename, or re-sending the same mode) keeps the references.
		await Directives(api => api.UpdateTimeframeAsync(timeframeId, new TimeframeUpdate(Title: "Core Hours", AutoInclusion: TimeframeInclusion.Availability), Ct));
		Assert.Equal(timeframeId, await AvailabilityOfAsync(first.Id));

		await Directives(api => api.UpdateTimeframeAsync(timeframeId, new TimeframeUpdate(AutoInclusion: TimeframeInclusion.College, AutoInclusionColleges: [ObjectiveCollege.Lore]), Ct));

		Assert.Null(await AvailabilityOfAsync(first.Id));
		Assert.Null(await AvailabilityOfAsync(second.Id));
		Assert.Equal(otherId, await AvailabilityOfAsync(bystander.Id));
	}

	[Fact]
	public async Task Deleting_the_timeframe_clears_the_directives_availability_where_the_change_feed_sees_it()
	{
		var timeframeId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var bystander = await Directives(api => api.CreateStandaloneAsync("Heron", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, timeframeId, Ct));

		var changes = await RecordAsync(() => Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteTimeframeAsync(timeframeId, Ct)));

		Assert.Null(await AvailabilityOfAsync(directive.Id));
		// The SetNull key alone would null the column silently; the tracked clear is what announces the directive.
		AssertAnnouncedModified(changes, nameof(StellarDirective), directive.Id);
		Assert.DoesNotContain(changes, change => change.Id == bystander.Id);
	}

	[Fact]
	public async Task Leaving_availability_mode_announces_the_cleared_directives_on_the_change_feed()
	{
		var timeframeId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		var stellar = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var lunar = await Directives(api => api.CreateLunarAsync("Tide", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(stellar.Id, timeframeId, Ct));
		await Directives(api => api.SetAvailabilityAsync(lunar.Id, timeframeId, Ct));

		var changes = await RecordAsync(() => Directives(api => api.UpdateTimeframeAsync(timeframeId, new TimeframeUpdate(AutoInclusion: TimeframeInclusion.None), Ct)));

		AssertAnnouncedModified(changes, nameof(StellarDirective), stellar.Id);
		AssertAnnouncedModified(changes, nameof(LunarDirective), lunar.Id);
	}

	[Fact]
	public async Task Deleting_the_owning_lunar_directive_clears_the_availability_where_the_change_feed_sees_it()
	{
		var lunar = await Directives(api => api.CreateLunarAsync("Office Law", cancellationToken: Ct));
		var timeframe = await Directives(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan("Office Hours", new TimeOnly(9, 0), new TimeOnly(17, 0), AutoInclusion: TimeframeInclusion.Availability),
			Ct));
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, timeframe.Id, Ct));

		// The lunar delete removes its timeframes at the database level, out of EF's sight; the delete clears the
		// availabilities pointing at them tracked (the SetNull key is only the backstop), so the directive is announced.
		var changes = await RecordAsync(() => Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteAsync(lunar.Id, Ct)));

		Assert.Null(await AvailabilityOfAsync(directive.Id));
		AssertAnnouncedModified(changes, nameof(StellarDirective), directive.Id);
		Assert.Contains(changes, change => change.Id == lunar.Id && change.Operation == EntityChangeOperation.Deleted);
	}

	[Fact]
	public async Task Deleting_the_owning_lunar_directive_through_the_sync_services_delete_action_clears_the_availability_where_the_change_feed_sees_it()
	{
		const string lunarTitle = "WatcherOfficeLaw";
		var lunar = await Directives(api => api.CreateLunarAsync(lunarTitle, cancellationToken: Ct));
		var timeframe = await Directives(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan("Office Hours", new TimeOnly(9, 0), new TimeOnly(17, 0), AutoInclusion: TimeframeInclusion.Availability),
			Ct));
		var directive = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var bystander = await Directives(api => api.CreateStandaloneAsync("Heron", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, timeframe.Id, Ct));

		// The watcher's delete-from-database action, driven directly: the directive (and, by the database cascade, its
		// timeframe) goes without the directive API ever running, so only the save hook can clear the availability
		// tracked here. A real note delete does not reach this action today. A lunar note is Quiet (its PUCK lives in
		// the frontmatter the deleted file no longer has), so discovery cannot recover the id from the path and would
		// ignore the removal; the candidate is therefore handed the directive's id, exactly as an identity-bearing
		// removal would reach the sync service.
		// Known gap (core/.DISCUSSION.md): discovery ignores a Quiet/Freeform lunar note delete today, so this proves the
		// save hook on the delete action only, not an end-to-end note delete.
		var note = Vault.MarkdownFilesUnder(Vault.AbsolutePath("")).Single(file => file.Contains(lunarTitle, StringComparison.OrdinalIgnoreCase));
		File.Delete(note);
		var inspected = await Vault.InspectAsync(note);
		Assert.NotNull(inspected);
		Assert.False(inspected.FileExists);
		var removal = inspected with { PathId = lunar.Id, SuggestedAction = VaultSyncAction.DeleteFromDatabase };
		var changes = await RecordAsync(() => Vault.WithScopeAsync(services => services.GetRequiredService<VaultWatcherSyncService>().ExecuteAsync(removal, "test", Ct)));

		Assert.False(await Vault.QueryAsync(context => context.Directives.AnyAsync(item => item.Id == lunar.Id, Ct)));
		Assert.Null(await AvailabilityOfAsync(directive.Id));
		AssertAnnouncedModified(changes, nameof(StellarDirective), directive.Id);
		Assert.DoesNotContain(changes, change => change.Id == bystander.Id);
	}

	[Fact]
	public async Task A_bare_context_timeframe_delete_or_mode_change_clears_availability_through_the_save_hook()
	{
		var deletedId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		var switchedId = await CreateTimeframeAsync(TimeframeInclusion.Availability, "Evenings");
		var first = await Directives(api => api.CreateStandaloneAsync("Raptor", cancellationToken: Ct));
		var second = await Directives(api => api.CreateLunarAsync("Tide", cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(first.Id, deletedId, Ct));
		await Directives(api => api.SetAvailabilityAsync(second.Id, switchedId, Ct));

		// No directive API at all: one unit of work on the bare context deletes one timeframe and switches the other out
		// of Availability mode, as any other pathway (CLI, a future importer) would.
		var changes = await RecordAsync(() => Vault.QueryAsync(async context =>
		{
			context.Timeframes.Remove(await context.Timeframes.SingleAsync(item => item.Id == deletedId, Ct));
			(await context.Timeframes.SingleAsync(item => item.Id == switchedId, Ct)).AutoInclusion = TimeframeInclusion.None;
			return await context.SaveChangesAsync(Ct);
		}));

		Assert.Null(await AvailabilityOfAsync(first.Id));
		Assert.Null(await AvailabilityOfAsync(second.Id));
		AssertAnnouncedModified(changes, nameof(StellarDirective), first.Id);
		AssertAnnouncedModified(changes, nameof(LunarDirective), second.Id);
	}

	[Fact]
	public async Task A_markdown_sync_of_the_directive_note_keeps_its_availability()
	{
		var timeframeId = await CreateTimeframeAsync(TimeframeInclusion.Availability);
		const string title = "AvailabilityReproDirective";
		var directive = await Directives(api => api.CreateStandaloneAsync(title, cancellationToken: Ct));
		await Directives(api => api.SetAvailabilityAsync(directive.Id, timeframeId, Ct));

		// Re-sync the directive's note exactly as the vault watcher would on a file change. Its frontmatter never
		// carries the availability, so a naive SetValues would clear it.
		var note = Vault.MarkdownFilesUnder(Vault.AbsolutePath("")).First(file => file.Contains(title, StringComparison.OrdinalIgnoreCase));
		await Vault.ReconcileAsync(note);

		Assert.Equal(timeframeId, await AvailabilityOfAsync(directive.Id));

		// A real frontmatter edit is applied while the availability still survives.
		await Directives(api => api.UpdateStellarAsync(directive.Id, new StellarDirectiveUpdate(Codename: "RAPTOR"), Ct));
		await Vault.ReconcileAsync(note);
		var reloaded = await Directives(api => api.GetAsync(directive.Id, Ct));
		Assert.Equal("RAPTOR", reloaded!.Codename);
		Assert.Equal(timeframeId, reloaded.AvailabilityTimeframeId);
	}
}
