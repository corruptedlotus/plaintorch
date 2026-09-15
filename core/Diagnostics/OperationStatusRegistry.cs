namespace Pleiades.Diagnostics;

/// <summary>
/// Holds the live set of active operation statuses (PEP108) and derives subsystem health. <see cref="Ingest"/>
/// diffs a report against the current statuses for its <c>(operation, scope)</c> to produce
/// raise/escalate/de-escalate/resolve transitions, so operations never track prior state themselves.
/// </summary>
/// <remarks>
/// A live status is keyed by <c>(OperationId, ScopeKey, ReasonCode)</c>: distinct reasons on the same scope are
/// distinct statuses. For auto-resolution to be exact, an operation should report all of the checks it evaluates
/// each run (passing and failing); a reason code absent from a report is left untouched.
/// </remarks>
public sealed class OperationStatusRegistry
{
	private const int ResolvedRingCapacity = 200;

	private readonly object _gate = new();
	private readonly Dictionary<(string Operation, string Scope, string Reason), OperationStatus> _active = [];
	private readonly Dictionary<string, OperationStatusDismissal> _dismissals = new(StringComparer.Ordinal);
	private readonly Queue<OperationStatusTransition> _recentResolved = new();
	private OperationHealth? _healthOverride;

	/// <summary>
	/// Ingests an operation report, updating live statuses and returning the transitions produced. A failing check
	/// raises a new status or escalates/de-escalates an existing one (a same-severity repeat only bumps the
	/// occurrence count); a passing check resolves any matching active status.
	/// </summary>
	public IReadOnlyList<OperationStatusTransition> Ingest(OperationReport report)
	{
		ArgumentNullException.ThrowIfNull(report);
		var now = DateTimeOffset.UtcNow;
		var transitions = new List<OperationStatusTransition>();

		lock (_gate)
		{
			foreach (var check in report.Checks)
			{
				var key = (report.OperationId, report.ScopeKey, check.ReasonCode);
				if (!check.Passed)
				{
					transitions.AddRange(RaiseOrEscalate(key, report, check, now));
				}
				else if (_active.Remove(key, out var resolved))
				{
					var transition = ToTransition(OperationStatusTransitionKind.Resolved, resolved with { LastObservedUtc = now }, resolved.Severity, now);
					transitions.Add(transition);
					PushResolved(transition);
				}
			}
		}

		return transitions;
	}

	/// <summary>Gets the active statuses, worst severity and most recent first. Includes dismissed statuses.</summary>
	public IReadOnlyList<OperationStatus> GetActiveStatuses()
	{
		lock (_gate)
		{
			return _active.Values
				.OrderByDescending(status => status.Severity)
				.ThenByDescending(status => status.LastObservedUtc)
				.ToList();
		}
	}

	/// <summary>
	/// Gets the active statuses paired with whether each is currently dismissed (PEP108 dismiss feature), worst
	/// severity and most recent first. A surface that offers a dismiss/restore affordance uses this so it can show
	/// dismissed statuses separately while excluding them from the health rollup.
	/// </summary>
	public IReadOnlyList<(OperationStatus Status, bool Dismissed)> GetActiveStatusesWithDismissal()
	{
		lock (_gate)
		{
			return _active.Values
				.OrderByDescending(status => status.Severity)
				.ThenByDescending(status => status.LastObservedUtc)
				.Select(status => (status, IsDismissedUnlocked(status)))
				.ToList();
		}
	}

	/// <summary>Gets recently-resolved transitions, newest first, from the bounded in-memory ring.</summary>
	public IReadOnlyList<OperationStatusTransition> GetRecentResolved()
	{
		lock (_gate)
		{
			return _recentResolved.Reverse().ToList();
		}
	}

	/// <summary>
	/// Derives subsystem health from active statuses, unless a lifecycle override is set. Dismissed statuses (PEP108
	/// dismiss feature) are excluded from the rollup, so a subsystem whose only remaining statuses are dismissed reads
	/// <see cref="OperationHealth.Ok"/>.
	/// </summary>
	public OperationHealth GetHealth()
	{
		lock (_gate)
		{
			if (_healthOverride is { } forced)
			{
				return forced;
			}

			var worst = _active.Values
				.Where(status => !IsDismissedUnlocked(status))
				.Select(status => (OperationSeverity?)status.Severity)
				.Max();

			if (worst is not { } severity)
			{
				return OperationHealth.Ok;
			}

			if (severity >= OperationSeverity.Error)
			{
				return OperationHealth.Issues;
			}

			return severity == OperationSeverity.Suspended
				? OperationHealth.Suspended
				: OperationHealth.Ok;
		}
	}

	/// <summary>
	/// Replaces the in-memory dismissal set with the supplied durable dismissals (PEP108 dismiss feature). Called when
	/// a vault session activates, before the startup scan re-raises its statuses, so dismissals apply immediately.
	/// </summary>
	public void LoadDismissals(IEnumerable<OperationStatusDismissal> dismissals)
	{
		ArgumentNullException.ThrowIfNull(dismissals);
		lock (_gate)
		{
			_dismissals.Clear();
			foreach (var dismissal in dismissals)
			{
				_dismissals[dismissal.Key] = dismissal;
			}
		}
	}

	/// <summary>Clears the in-memory dismissal set (for example when the active vault session ends).</summary>
	public void ClearDismissals()
	{
		lock (_gate)
		{
			_dismissals.Clear();
		}
	}

	/// <summary>
	/// Records a dismissal for a status identity and returns the stored record so the caller can persist it. For an
	/// <see cref="OperationStatusDismissalScope.Instance"/> dismissal the current active status's structural fingerprint is
	/// captured (so the snooze lifts when a different problem arises); this returns <see langword="null"/>
	/// when no such status is currently active — there is nothing to dismiss. File and Reason dismissals always record.
	/// </summary>
	public OperationStatusDismissal? Dismiss(OperationStatusDismissalScope scope, string operationId, string scopeKey, string reasonCode)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
		ArgumentNullException.ThrowIfNull(scopeKey);
		ArgumentNullException.ThrowIfNull(reasonCode);

		lock (_gate)
		{
			string? fingerprint = null;
			if (scope == OperationStatusDismissalScope.Instance)
			{
				if (!_active.TryGetValue((operationId, scopeKey, reasonCode), out var status))
				{
					return null;
				}

				fingerprint = status.Fingerprint;
			}

			var dismissal = new OperationStatusDismissal(scope, operationId, scopeKey, reasonCode, fingerprint, DateTimeOffset.UtcNow);
			_dismissals[dismissal.Key] = dismissal;
			return dismissal;
		}
	}

	/// <summary>
	/// Removes a dismissal, returning the record that was removed, or <see langword="null"/> if none matched. Restoring
	/// a dismissed status makes it count and surface again.
	/// </summary>
	public OperationStatusDismissal? Restore(OperationStatusDismissalScope scope, string operationId, string scopeKey, string reasonCode)
	{
		var key = OperationStatusDismissalKey.Compose(scope, operationId, scopeKey, reasonCode);
		lock (_gate)
		{
			return _dismissals.Remove(key, out var removed) ? removed : null;
		}
	}

	/// <summary>Determines whether any active dismissal currently suppresses the supplied status.</summary>
	public bool IsDismissed(OperationStatus status)
	{
		ArgumentNullException.ThrowIfNull(status);
		lock (_gate)
		{
			return IsDismissedUnlocked(status);
		}
	}

	private bool IsDismissedUnlocked(OperationStatus status)
	{
		foreach (var dismissal in _dismissals.Values)
		{
			if (dismissal.Matches(status))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Sets a lifecycle health override that wins over the derived rollup (for example a watcher forcing
	/// <see cref="OperationHealth.Offline"/> when stopped), or clears it with <see langword="null"/>.
	/// </summary>
	public void SetHealthOverride(OperationHealth? health)
	{
		lock (_gate)
		{
			_healthOverride = health;
		}
	}

	private IEnumerable<OperationStatusTransition> RaiseOrEscalate(
		(string Operation, string Scope, string Reason) key,
		OperationReport report,
		OperationCheck check,
		DateTimeOffset now)
	{
		if (_active.TryGetValue(key, out var existing))
		{
			var updated = existing with
			{
				Severity = check.Severity,
				Files = check.Files ?? existing.Files,
				EntityId = check.EntityId ?? existing.EntityId,
				Detail = check.Detail,
				Fingerprint = check.Fingerprint,
				OccurrenceCount = existing.OccurrenceCount + 1,
				LastObservedUtc = now,
			};
			_active[key] = updated;

			if (check.Severity == existing.Severity)
			{
				yield break;
			}

			var kind = check.Severity > existing.Severity
				? OperationStatusTransitionKind.Escalated
				: OperationStatusTransitionKind.Deescalated;
			yield return ToTransition(kind, updated, existing.Severity, now);
			yield break;
		}

		var raised = new OperationStatus(
			report.OperationId,
			report.ScopeKey,
			check.ReasonCode,
			check.Severity,
			check.Files ?? [],
			check.EntityId,
			check.Detail,
			check.Fingerprint,
			OccurrenceCount: 1,
			FirstRaisedUtc: now,
			LastObservedUtc: now);
		_active[key] = raised;
		yield return ToTransition(OperationStatusTransitionKind.Raised, raised, previousSeverity: null, now);
	}

	private void PushResolved(OperationStatusTransition transition)
	{
		_recentResolved.Enqueue(transition);
		while (_recentResolved.Count > ResolvedRingCapacity)
		{
			_recentResolved.Dequeue();
		}
	}

	private static OperationStatusTransition ToTransition(
		OperationStatusTransitionKind kind,
		OperationStatus status,
		OperationSeverity? previousSeverity,
		DateTimeOffset occurredUtc)
		=> new(
			kind,
			status.OperationId,
			status.ScopeKey,
			status.ReasonCode,
			status.Severity,
			previousSeverity,
			status.Files,
			status.EntityId,
			status.Detail,
			status.OccurrenceCount,
			occurredUtc);
}
