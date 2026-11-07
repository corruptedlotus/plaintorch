namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// The one gate on whether time acts on an item (PEP103 patch 6, user ruling): an item is temporally active while it is open
/// or started, and an item that is finished or set aside has its temporal activation switched off altogether. Every rule
/// that lets time act on an item asks here, by the item's lifecycle state, and never by naming a status.
/// </summary>
/// <remarks>
/// What the gate switches off for an inactive item:
/// <list type="bullet">
/// <item><description>Orbit materialization: a fate's eventives and a decree's attentives are neither projected, hardened by
/// time or by interaction, nor added by hand, and the cached next occurrence is cleared.</description></item>
/// <item><description>Its due (<see cref="DueActs"/>): its terminative is neither projected nor hardened, and one already
/// hardened is resolved (PEP103 patch 7).</description></item>
/// <item><description>Lunar reflection: a decree that is not active is never drawn into a cycle.</description></item>
/// </list>
/// What switching off leaves: an occurrence already hardened stays, and can still be resolved; only new ones stop (user
/// ruling). A checkpoint's due acts until it finishes, since passing its due is what sets it aside, and its toll is the
/// reconciler's (<c>DependencyReconciler.Unlocks</c>): once a checkpoint has finished, its due no longer brings its toll due.
/// A lunar directive has no ordinary lifecycle (<see cref="LunarDirectiveStatus"/>): reflection under it runs only while it
/// is Active, and its timeframes act until it is Stale (<see cref="TimeframesAct"/>). How an active item's occurrences show — an opted-out fate's stamped OptOut, and hardened by
/// time only under the auto-materialise-optout preference — is presentation, not activation.
/// </remarks>
public static class TemporalActivation
{
	/// <summary>
	/// Whether an item in <paramref name="state"/> is temporally active: open or started.
	/// </summary>
	public static bool IsActive(LifecycleState state) => state is LifecycleState.Open or LifecycleState.Started;

	/// <summary>
	/// Whether an item holding <paramref name="status"/> is temporally active.
	/// </summary>
	public static bool IsActive(Enum status) => IsActive(LifecycleDeclarations.StateOf(status));

	/// <summary>
	/// The members of a status enum that leave an item temporally active, for a query to filter on in the store.
	/// </summary>
	public static IReadOnlyList<TStatus> Statuses<TStatus>()
		where TStatus : struct, Enum
		=> LifecycleDeclarations.MembersOf(typeof(TStatus))
			.Where(member => IsActive(member.State))
			.Select(member => (TStatus)member.Status)
			.ToList();

	/// <summary>
	/// Whether an item's due acts (PEP103 patch 7): its terminative is projected and hardened, and stays pending. A due acts
	/// while its item is temporally active, and also while the item is set aside by nothing but its own due having passed
	/// (<see cref="IDueOwner.DueDefers"/>, a checkpoint still locked), so a checkpoint's due acts until it unlocks.
	/// </summary>
	public static bool DueActs(IDueOwner owner, LifecycleState state)
	{
		ArgumentNullException.ThrowIfNull(owner);
		return IsActive(state) || (state == LifecycleState.Deferred && owner.DueDefers);
	}

	/// <summary>
	/// Whether a lunar directive's timeframes act (user ruling, PEP103 patch 6): while it is not finished. A Stale lunar
	/// directive's timeframes are never candidates of a cycle, never active, and lend no affinity; On Hold keeps them, as it
	/// pauses reflection and nothing else.
	/// </summary>
	public static bool TimeframesAct(LunarDirectiveStatus status) => LifecycleDeclarations.StateOf(status) != LifecycleState.Finished;

	/// <summary>
	/// The lunar directive statuses under which its timeframes act (<see cref="TimeframesAct"/>), for a query to filter on.
	/// </summary>
	public static IReadOnlyList<LunarDirectiveStatus> TimeframeStatuses()
		=> LifecycleDeclarations.MembersOf(typeof(LunarDirectiveStatus))
			.Select(member => (LunarDirectiveStatus)member.Status)
			.Where(TimeframesAct)
			.ToList();
}
