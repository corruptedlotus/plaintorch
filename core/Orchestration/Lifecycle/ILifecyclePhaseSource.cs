namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies lifecycle begin/finish state for entity kinds whose phases cannot come purely from a status value
/// (PEP101) — for example eventives, whose "passing" is temporal rather than stateful, or checkpoints, which finish by
/// unlocking. Registered sources are consulted by <see cref="EntityLifecycleResolver"/> before the attribute-driven
/// status path.
/// </summary>
public interface ILifecyclePhaseSource
{
	/// <summary>
	/// Gets the lifecycle phases the handled kind passes through: <see cref="LifecyclePhase.Begin"/> and
	/// <see cref="LifecyclePhase.Finish"/> for a kind that spans time, <see cref="LifecyclePhase.Finish"/> alone for a
	/// one-shot kind that cannot be started, only finished.
	/// </summary>
	IReadOnlySet<LifecyclePhase> Phases { get; }

	/// <summary>
	/// Determines whether this source handles the given entity type.
	/// </summary>
	bool Handles(Type entityType);

	/// <summary>
	/// Determines whether the entity has begun. A finished entity has begun, one-shot kinds included.
	/// </summary>
	bool HasBegun(object entity);

	/// <summary>
	/// Determines whether the entity has finished.
	/// </summary>
	bool HasFinished(object entity);
}
