namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies the lifecycle of <see cref="Checkpoint"/>s (PEP101). A checkpoint is one-shot: it cannot be started, only
/// finished, and its finish is its unlock — the emitted signal the dependency reconciler maintains once its incoming
/// dependencies, toll and external condition are all met. A checkpoint that re-locks is no longer finished.
/// </summary>
/// <remarks>
/// Having no begin of its own, a checkpoint counts as begun exactly when it has finished, keeping the resolver's rule
/// that a finish implies a begin. The dependency system already reads a checkpoint this way: an edge from it is
/// satisfied by its unlock and an edge into it gates its unlock, so neither side carries a trigger or a constraint —
/// with a single phase there is nothing to choose.
/// </remarks>
public sealed class CheckpointLifecyclePhaseSource : ILifecyclePhaseSource
{
	/// <inheritdoc />
	public IReadOnlySet<LifecyclePhase> Phases { get; } = new HashSet<LifecyclePhase> { LifecyclePhase.Finish };

	/// <inheritdoc />
	public bool Handles(Type entityType) => entityType == typeof(Checkpoint);

	/// <inheritdoc />
	public bool HasBegun(object entity) => HasFinished(entity);

	/// <inheritdoc />
	public bool HasFinished(object entity) => ((Checkpoint)entity).Unlocked;
}
