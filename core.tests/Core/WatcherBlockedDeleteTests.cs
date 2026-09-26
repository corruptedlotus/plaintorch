using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A reported dev/phase2d bug, reproduced here and now fixed: the note of a begun objective was deleted while the objective
/// still had executive records. The watcher's file-driven delete archived the objective into the database graveyard in its
/// own <c>SaveChanges</c>, then tried to remove the row — which the database refused (the executives reference it through
/// a restricting foreign key). The graveyard entry was already committed; the row stayed; the path was flagged
/// <c>sync-failed</c>; and the retry sweep re-attempted it at the capped cadence forever. The sandbox vault had collected
/// 1,936 graveyard entries for one objective (one a minute whenever a core served it), and a failed delete in the startup
/// sweep's single scope stayed tracked, so the next candidate's save re-flushed it and failed too.
///
/// Fixed three ways, each pinned below: the graveyard entry is <em>staged</em> and commits in the one save that removes
/// the entity (atomic — a refused removal leaves no entry); a delete the database would refuse is recognised up front from
/// the model's restricting relationships and surfaces once as a standing <c>delete-blocked</c> status that the retry sweep
/// leaves alone (resolved by restoring the note, or by the next sweep once nothing references the entity); and a failed
/// candidate discards whatever it staged, so it can never fail the rest of a sweep.
///
/// Executives no longer block at all (a deleted item keeps its ended work and drops its live plans; see
/// <see cref="IncentiveDeletionTests"/>), so the blocked objective here is one another incentive names as its parent.
/// </summary>
public sealed class WatcherBlockedDeleteTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string NoteOf(string title) => $"Objectives/{title}.md";

	/// <summary>Seeds a begun objective (its note on disk) that another objective names as its parent — a restricting relationship.</summary>
	private async Task<Objective> SeedBegunObjectiveWithChildAsync(string title)
	{
		var objective = await SeedBegunObjectiveAsync(title);
		var child = await Vault.SeedStandaloneObjectiveAsync(title + " Child");
		await Vault.QueryAsync(context => context.Incentives
			.Where(item => item.Id == child.Id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ParentIncentiveId, objective.Id), Token));
		return objective;
	}

	private async Task<Objective> SeedBegunObjectiveAsync(string title)
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync(title);
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		return objective;
	}

	private Task<bool> ObjectiveExistsAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == id, Token));

	private Task<int> GraveyardEntriesForAsync(string id)
		=> Vault.QueryAsync(context => context.DatabaseGraveyardEntries.CountAsync(item => item.EntityId == id, Token));

	private Task<int> ChildrenOfAsync(string id)
		=> Vault.QueryAsync(context => context.Incentives.CountAsync(item => item.ParentIncentiveId == id, Token));

	private Task<int> ReleaseChildrenOfAsync(string id)
		=> Vault.QueryAsync(context => context.Incentives
			.Where(item => item.ParentIncentiveId == id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ParentIncentiveId, (string?)null), Token));

	private IReadOnlyList<OperationStatus> StatusesAt(string vaultRelativePath)
		=> Vault.GetSingleton<OperationStatusRegistry>()
			.GetActiveStatuses()
			.Where(status => string.Equals(status.ScopeKey, Vault.AbsolutePath(vaultRelativePath), StringComparison.OrdinalIgnoreCase))
			.ToList();

	[Fact] // Fixed — the removal is checked against the model's restricting relationships first; nothing is written when it is blocked.
	public async Task Deleting_the_note_of_an_objective_with_child_incentives_writes_nothing_and_keeps_it()
	{
		var objective = await SeedBegunObjectiveWithChildAsync("Blocked");
		var path = Vault.AbsolutePath(NoteOf("Blocked"));
		File.Delete(path);

		// Two passes: the live delete event, then a later re-check of the same path.
		await Vault.ReconcileWithIssuesAsync(path);
		await Vault.ReconcileWithIssuesAsync(path);

		Assert.True(await ObjectiveExistsAsync(objective.Id));
		Assert.Equal(1, await ChildrenOfAsync(objective.Id));
		Assert.Equal(0, await GraveyardEntriesForAsync(objective.Id));
	}

	[Fact] // Fixed — a blocked delete is its own standing reason, raised once and refreshed, never re-queued by the retry sweep.
	public async Task The_blocked_delete_surfaces_once_as_a_standing_status_the_retry_sweep_leaves_alone()
	{
		var objective = await SeedBegunObjectiveWithChildAsync("Blocked");
		var path = Vault.AbsolutePath(NoteOf("Blocked"));
		File.Delete(path);

		await Vault.ReconcileWithIssuesAsync(path);
		await Vault.SweepWithIssuesAsync();

		var status = Assert.Single(StatusesAt(NoteOf("Blocked")));
		Assert.Equal(WatcherOperations.DeleteBlocked, status.ReasonCode);
		Assert.Equal(OperationSeverity.Error, status.Severity);
		Assert.Equal(objective.Id, status.EntityId);
		Assert.Contains("Incentive", status.Detail);
		// The sweep refreshed the one status raised by the live event rather than raising another.
		Assert.Equal(2, status.OccurrenceCount);

		// However long it stays stuck, the retry sweep never re-queues it — the minute-by-minute re-attempts are gone.
		var registry = Vault.GetSingleton<OperationStatusRegistry>();
		Assert.False(WatcherRetryScheduler.IsRetryable(status));
		Assert.Empty(new WatcherRetryScheduler().DuePaths(registry.GetActiveStatuses(), DateTimeOffset.UtcNow.AddDays(1)));
	}

	[Fact] // Fixed — restoring the note is a clean sync at the path, which clears the standing status.
	public async Task Restoring_the_note_resolves_the_blocked_delete()
	{
		var objective = await SeedBegunObjectiveWithChildAsync("Blocked");
		var note = NoteOf("Blocked");
		var content = Vault.ReadVaultFile(note);
		File.Delete(Vault.AbsolutePath(note));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath(note));
		Assert.Single(StatusesAt(note));

		Vault.WriteVaultFile(note, content);
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath(note));

		Assert.Empty(StatusesAt(note));
		Assert.True(await ObjectiveExistsAsync(objective.Id));
		Assert.Equal(0, await GraveyardEntriesForAsync(objective.Id));
	}

	[Fact] // Fixed — once nothing references the entity, the next sweep removes it with exactly one graveyard entry.
	public async Task Once_nothing_references_it_the_next_sweep_completes_the_removal()
	{
		var objective = await SeedBegunObjectiveWithChildAsync("Blocked");
		File.Delete(Vault.AbsolutePath(NoteOf("Blocked")));
		await Vault.SweepWithIssuesAsync();
		Assert.Single(StatusesAt(NoteOf("Blocked")));

		await ReleaseChildrenOfAsync(objective.Id);
		await Vault.SweepWithIssuesAsync();

		Assert.False(await ObjectiveExistsAsync(objective.Id));
		Assert.Equal(1, await GraveyardEntriesForAsync(objective.Id));
		Assert.Empty(StatusesAt(NoteOf("Blocked")));
	}

	[Fact] // Fixed — the blocked delete sits beside an ordinary one in the same sweep, and only the blocked one is held back.
	public async Task A_blocked_delete_does_not_hold_back_the_rest_of_the_sweep()
	{
		// Alpha sorts first, so its delete runs before Beta's in the sweep's one scope.
		var alpha = await SeedBegunObjectiveWithChildAsync("Alpha");
		var beta = await SeedBegunObjectiveAsync("Beta");
		File.Delete(Vault.AbsolutePath(NoteOf("Alpha")));
		File.Delete(Vault.AbsolutePath(NoteOf("Beta")));

		await Vault.SweepWithIssuesAsync();

		Assert.True(await ObjectiveExistsAsync(alpha.Id));
		Assert.Equal(0, await GraveyardEntriesForAsync(alpha.Id));
		Assert.False(await ObjectiveExistsAsync(beta.Id));
		Assert.Equal(1, await GraveyardEntriesForAsync(beta.Id));
		Assert.Equal(WatcherOperations.DeleteBlocked, Assert.Single(StatusesAt(NoteOf("Alpha"))).ReasonCode);
		Assert.Empty(StatusesAt(NoteOf("Beta")));
	}

	[Fact] // Fixed — the graveyard entry commits with the removal, and a failed candidate discards what it staged.
	public async Task A_delete_the_database_refuses_leaves_no_graveyard_entry_and_does_not_fail_the_next_candidate()
	{
		// A refusal the up-front check cannot foresee, so the removal really reaches SaveChanges and really fails there.
		var alpha = await SeedBegunObjectiveAsync("Alpha");
		var beta = await SeedBegunObjectiveAsync("Beta");
		await RefuseDeletionOfAsync(alpha.Id);
		File.Delete(Vault.AbsolutePath(NoteOf("Alpha")));
		File.Delete(Vault.AbsolutePath(NoteOf("Beta")));

		await Vault.SweepWithIssuesAsync();

		// Atomic: the refused removal rolled its staged graveyard entry back with it.
		Assert.True(await ObjectiveExistsAsync(alpha.Id));
		Assert.Equal(0, await GraveyardEntriesForAsync(alpha.Id));
		// An unforeseen failure stays an ordinary, retryable sync failure.
		var status = Assert.Single(StatusesAt(NoteOf("Alpha")));
		Assert.Equal(WatcherOperations.SyncFailed, status.ReasonCode);
		Assert.True(WatcherRetryScheduler.IsRetryable(status));
		// Beta ran in the same scope after Alpha failed, and was not dragged down by Alpha's refused changes.
		Assert.False(await ObjectiveExistsAsync(beta.Id));
		Assert.Equal(1, await GraveyardEntriesForAsync(beta.Id));
		Assert.Empty(StatusesAt(NoteOf("Beta")));
	}

	[Fact] // Fixed — the API deletes share the staged archive, so a refused API delete leaves no graveyard entry either.
	public async Task A_refused_api_delete_leaves_no_graveyard_entry_and_a_clean_one_leaves_exactly_one()
	{
		var refused = await Vault.SeedStandaloneObjectiveAsync("Refused");
		var plain = await Vault.SeedStandaloneObjectiveAsync("Plain");
		await RefuseDeletionOfAsync(refused.Id);

		await Assert.ThrowsAnyAsync<DbUpdateException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.DeleteAsync(refused.Id, Token)));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().DeleteAsync(plain.Id, Token));

		Assert.True(await ObjectiveExistsAsync(refused.Id));
		Assert.Equal(0, await GraveyardEntriesForAsync(refused.Id));
		Assert.False(await ObjectiveExistsAsync(plain.Id));
		Assert.Equal(1, await GraveyardEntriesForAsync(plain.Id));
	}

	/// <summary>
	/// Makes the database refuse to delete one incentive row — a stand-in for any refusal the model's relationships do not
	/// describe, so a removal really reaches <c>SaveChanges</c> and really fails there.
	/// </summary>
	private Task RefuseDeletionOfAsync(string incentiveId)
	{
		var sql = "CREATE TRIGGER refuse_" + incentiveId + " BEFORE DELETE ON \"Incentives\" WHEN OLD.\"Id\" = '" + incentiveId
			+ "' BEGIN SELECT RAISE(ABORT, 'refused by test'); END;";
		return Vault.QueryAsync(context => context.Database.ExecuteSqlRawAsync(sql, Token));
	}
}
