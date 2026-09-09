using Pleiades.Diagnostics;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Turns the live operation-status set (PEP108) into the watcher's retry queue: the active issues <em>are</em> the
/// work list, so there is no separate retry-tracking structure to drift out of sync with what the user sees. Each
/// interval the watcher asks which flagged scopes are due to be re-checked; a scope re-inspected cleanly resolves its
/// own status through the ordinary diff-based reporting, and one that still fails simply refreshes — its occurrence
/// count grows, its backoff widens, and it keeps being retried indefinitely at the capped cadence.
/// </summary>
/// <remarks>
/// Only per-path reconcile issues are retryable. Global-scope conditions (a structural vault failure, a failed tick)
/// are driven by the watcher's own supervision loop, not by re-queuing a path; and a <see cref="WatcherOperations.ForeignFile"/>
/// advisory is deliberately excluded — leaving the unmanaged file in place is already the successful outcome, so
/// re-checking it would never resolve the flag and would only churn.
/// </remarks>
public sealed class WatcherRetryScheduler
{
	// Escalating backoff keyed off the status's own occurrence count, capped so a genuinely stuck scope keeps being
	// retried forever at a slow, cheap cadence rather than either giving up or hot-looping.
	private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

	/// <summary>
	/// Selects the distinct scope paths whose flagged issues are due to be re-checked at <paramref name="now"/>.
	/// </summary>
	public IReadOnlyList<string> DuePaths(IReadOnlyList<OperationStatus> activeStatuses, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(activeStatuses);

		return activeStatuses
			.Where(IsRetryable)
			.Where(status => now - status.LastObservedUtc >= BackoffFor(status.OccurrenceCount))
			.Select(status => status.ScopeKey)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>Determines whether a status participates in path-based retry.</summary>
	public static bool IsRetryable(OperationStatus status)
	{
		ArgumentNullException.ThrowIfNull(status);
		return string.Equals(status.OperationId, WatcherOperations.Reconcile, StringComparison.Ordinal)
			&& !string.Equals(status.ScopeKey, WatcherOperations.GlobalScope, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(status.ScopeKey)
			&& !string.Equals(status.ReasonCode, WatcherOperations.ForeignFile, StringComparison.Ordinal);
	}

	/// <summary>Computes the retry backoff for a status that has been observed <paramref name="occurrenceCount"/> times.</summary>
	public static TimeSpan BackoffFor(int occurrenceCount)
	{
		var steps = Math.Max(0, occurrenceCount - 1);
		// Guard the shift against overflow before it ever reaches the cap.
		if (steps >= 32)
		{
			return MaxBackoff;
		}

		var scaledTicks = InitialBackoff.Ticks * (1L << steps);
		return scaledTicks >= MaxBackoff.Ticks || scaledTicks < 0
			? MaxBackoff
			: TimeSpan.FromTicks(scaledTicks);
	}
}
