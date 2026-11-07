namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies the lifecycle state of <see cref="Checkpoint"/>s (PEP103). A checkpoint can finish or be deferred, but never
/// starts. It finishes by unlocking — the signal the dependency reconciler maintains once its incoming dependencies, toll
/// and external condition are all met — and its due is the one way it fails: a checkpoint still locked once its due moment
/// has passed is deferred.
/// </summary>
/// <remarks>
/// A deferred checkpoint can still finish. Its toll, waived while there was time, is owed once the due passes, so paying it
/// (with its other conditions met) unlocks the checkpoint late; moving the due later reopens it. Having no start of its own,
/// a checkpoint's dependencies carry no trigger or constraint: an edge from it is satisfied by its unlock, and an edge into
/// it holds back its unlock.
/// </remarks>
public sealed class CheckpointLifecycleStateSource : ILifecycleStateSource
{
	/// <inheritdoc />
	public IReadOnlySet<LifecycleState> States { get; } = new HashSet<LifecycleState>
	{
		LifecycleState.Deferred,
		LifecycleState.Open,
		LifecycleState.Finished,
	};

	/// <inheritdoc />
	/// <remarks>A checkpoint's state is derived from its unlock and its due, so no status of its own can be set.</remarks>
	public Type? StatusType => null;

	/// <inheritdoc />
	public bool Handles(Type entityType) => entityType == typeof(Checkpoint);

	/// <inheritdoc />
	public LifecycleState StateOf(object entity)
	{
		var checkpoint = (Checkpoint)entity;
		if (checkpoint.Unlocked)
		{
			return LifecycleState.Finished;
		}

		// Civil wall-clock time, as the reconciler reads the due when it decides whether the toll is owed.
		return checkpoint.Due is { } due && DateTime.Now >= due.Moment ? LifecycleState.Deferred : LifecycleState.Open;
	}
}
