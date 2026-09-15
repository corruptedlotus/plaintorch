using Pleiades.Diagnostics;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The retry queue is the live issue set: the scheduler decides which flagged scopes are due to be re-checked, so the
/// watcher needs no separate attempt counter and the user's visible queue and the retried work are the same thing.
/// </summary>
public sealed class WatcherRetrySchedulerTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

	private static OperationStatus Status(
		string reasonCode,
		string scopeKey,
		string operationId = WatcherOperations.Reconcile,
		int occurrenceCount = 1,
		TimeSpan? sinceObserved = null)
	{
		var lastObserved = Now - (sinceObserved ?? TimeSpan.FromHours(1));
		return new OperationStatus(
			operationId,
			scopeKey,
			reasonCode,
			OperationSeverity.Error,
			Files: [scopeKey],
			EntityId: null,
			Detail: null,
			Fingerprint: null,
			OccurrenceCount: occurrenceCount,
			FirstRaisedUtc: lastObserved,
			LastObservedUtc: lastObserved);
	}

	[Fact]
	public void A_stale_reconcile_issue_is_due_for_retry()
	{
		var scheduler = new WatcherRetryScheduler();
		var due = scheduler.DuePaths([Status(WatcherOperations.SyncFailed, "C:/vault/Objectives/A.md")], Now);
		Assert.Equal(["C:/vault/Objectives/A.md"], due);
	}

	[Fact]
	public void A_recently_observed_issue_is_not_yet_due()
	{
		var scheduler = new WatcherRetryScheduler();
		// Observed 1s ago with the shortest (occurrence 1 => 2s) backoff: not yet due.
		var due = scheduler.DuePaths(
			[Status(WatcherOperations.SyncFailed, "C:/vault/Objectives/A.md", occurrenceCount: 1, sinceObserved: TimeSpan.FromSeconds(1))],
			Now);
		Assert.Empty(due);
	}

	[Fact]
	public void A_foreign_file_advisory_is_never_retried()
	{
		// Leaving the unmanaged file in place is the successful outcome; re-checking it would never resolve and only churn.
		var scheduler = new WatcherRetryScheduler();
		Assert.False(WatcherRetryScheduler.IsRetryable(Status(WatcherOperations.ForeignFile, "C:/vault/Notes/Stray.md")));
		Assert.Empty(scheduler.DuePaths([Status(WatcherOperations.ForeignFile, "C:/vault/Notes/Stray.md")], Now));
	}

	[Fact]
	public void Global_scope_and_non_reconcile_conditions_are_not_path_retried()
	{
		var scheduler = new WatcherRetryScheduler();
		// A structural (tier-2) condition is driven by the supervision loop, not by re-queuing a path.
		Assert.False(WatcherRetryScheduler.IsRetryable(
			Status(WatcherOperations.VaultInaccessible, WatcherOperations.GlobalScope, WatcherOperations.VaultAccess)));
		// A global-scope reconcile reason (e.g. a failed tick) carries no path to re-queue.
		Assert.False(WatcherRetryScheduler.IsRetryable(Status(WatcherOperations.TickFailed, WatcherOperations.GlobalScope)));
		Assert.Empty(scheduler.DuePaths(
			[
				Status(WatcherOperations.VaultInaccessible, WatcherOperations.GlobalScope, WatcherOperations.VaultAccess),
				Status(WatcherOperations.TickFailed, WatcherOperations.GlobalScope),
			],
			Now));
	}

	[Fact]
	public void Multiple_reasons_on_one_scope_collapse_to_a_single_requeue()
	{
		var scheduler = new WatcherRetryScheduler();
		var due = scheduler.DuePaths(
			[
				Status(WatcherOperations.SyncFailed, "C:/vault/Objectives/A.md"),
				Status(WatcherOperations.MarkdownInvalid, "C:/vault/Objectives/A.md"),
			],
			Now);
		Assert.Equal(["C:/vault/Objectives/A.md"], due);
	}

	[Fact]
	public void Backoff_escalates_with_occurrence_count_and_caps()
	{
		Assert.Equal(TimeSpan.FromSeconds(2), WatcherRetryScheduler.BackoffFor(1));
		Assert.Equal(TimeSpan.FromSeconds(4), WatcherRetryScheduler.BackoffFor(2));
		Assert.Equal(TimeSpan.FromSeconds(8), WatcherRetryScheduler.BackoffFor(3));
		// Caps rather than growing unbounded — and never overflows for a long-stuck scope.
		Assert.Equal(TimeSpan.FromSeconds(60), WatcherRetryScheduler.BackoffFor(100));
		Assert.Equal(TimeSpan.FromSeconds(60), WatcherRetryScheduler.BackoffFor(int.MaxValue));
	}

	[Fact]
	public void A_long_stuck_issue_keeps_being_retried_at_the_capped_cadence()
	{
		var scheduler = new WatcherRetryScheduler();
		// Observed 61s ago after many failures: past the 60s cap, so still due — retry is indefinite, never abandoned.
		var due = scheduler.DuePaths(
			[Status(WatcherOperations.SyncFailed, "C:/vault/Objectives/A.md", occurrenceCount: 50, sinceObserved: TimeSpan.FromSeconds(61))],
			Now);
		Assert.Equal(["C:/vault/Objectives/A.md"], due);
	}
}
