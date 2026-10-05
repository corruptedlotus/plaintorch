namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// A move between two lifecycle states of one kind (PEP103), read the same way by every rule that judges a change: the
/// dependency gate, the records and the state decisions.
/// </summary>
/// <remarks>
/// Only <see cref="LifecycleState.Started"/> and <see cref="LifecycleState.Finished"/> are entered or left: they are what
/// dependencies attach to and what plans and records place in time, while <see cref="LifecycleState.Open"/> is where the main
/// line begins. A deferral is a side exit that keeps the item's place on the main line, so deferring enters and leaves
/// nothing there. A resume enters the progress states up to its target as though from before Open, since a deferred item has
/// not started; whether it had started before it was set aside is not part of the transition.
/// </remarks>
/// <param name="From">The state the item leaves.</param>
/// <param name="To">The state the item moves to.</param>
/// <param name="KindStates">The states the item's kind passes through.</param>
public readonly record struct LifecycleTransition(LifecycleState From, LifecycleState To, IReadOnlySet<LifecycleState> KindStates)
{
	private static readonly LifecycleState[] Progress = [LifecycleState.Started, LifecycleState.Finished];

	/// <summary>
	/// Gets whether the move changes state at all. A shift between two statuses of one state is no transition.
	/// </summary>
	public bool IsChange => From != To;

	/// <summary>
	/// Gets whether the move sets the item aside.
	/// </summary>
	public bool Defers => To == LifecycleState.Deferred && From != LifecycleState.Deferred;

	/// <summary>
	/// Gets whether the move brings a deferred item back to the main line.
	/// </summary>
	public bool Resumes => From == LifecycleState.Deferred && To != LifecycleState.Deferred;

	/// <summary>
	/// Gets the progress states the move reaches, in order: each one the kind passes through above where the item stood (a
	/// deferred item counts as before Open) up to <see cref="To"/>. Opening an item straight into Finished enters Started and
	/// Finished; a checkpoint, which never starts, only ever enters Finished; deferring enters nothing.
	/// </summary>
	public IReadOnlyList<LifecycleState> Entered
	{
		get
		{
			if (To == LifecycleState.Deferred)
			{
				return [];
			}

			var from = From == LifecycleState.Deferred ? LifecycleState.Open : From;
			var kindStates = KindStates;
			var to = To;
			return Progress.Where(state => kindStates.Contains(state) && state > from && state <= to).ToList();
		}
	}

	/// <summary>
	/// Gets the progress states a move back along the main line leaves, latest first: pulling a finished item back to Open
	/// leaves Finished and Started. A deferral leaves nothing, since the item keeps its place on the main line, and neither
	/// does a resume, whose place before the deferral is not part of the transition.
	/// </summary>
	public IReadOnlyList<LifecycleState> Left
	{
		get
		{
			if (From == LifecycleState.Deferred || To == LifecycleState.Deferred)
			{
				return [];
			}

			var kindStates = KindStates;
			var (from, to) = (From, To);
			return Enumerable.Reverse(Progress).Where(state => kindStates.Contains(state) && state <= from && state > to).ToList();
		}
	}
}
