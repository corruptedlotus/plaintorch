namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies the lifecycle state of a kind whose state is not a plain status value (PEP103): an eventive, whose passing is
/// temporal, or a checkpoint, which finishes by unlocking and is deferred by its due. Registered sources are consulted by
/// <see cref="EntityLifecycleResolver"/> before the declaration-driven status path.
/// </summary>
public interface ILifecycleStateSource
{
	/// <summary>
	/// Gets the states the handled kind passes through.
	/// </summary>
	IReadOnlySet<LifecycleState> States { get; }

	/// <summary>
	/// Gets the status enum the core sets when it moves the handled kind to a state of its own accord, or
	/// <see langword="null"/> when no status of the kind can be set to reach a state (a checkpoint's state is derived).
	/// Its members declare their states like any lifecycle status enum.
	/// </summary>
	Type? StatusType { get; }

	/// <summary>
	/// Determines whether this source handles the given entity type.
	/// </summary>
	bool Handles(Type entityType);

	/// <summary>
	/// Gets the entity's current lifecycle state.
	/// </summary>
	LifecycleState StateOf(object entity);
}
