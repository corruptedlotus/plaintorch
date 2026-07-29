namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies lifecycle begin/finish state for entity kinds whose phases cannot come purely from a status value
/// (PEP101) — for example eventives, whose "passing" is temporal rather than stateful. Registered sources are
/// consulted by <see cref="EntityLifecycleResolver"/> before the attribute-driven status path.
/// </summary>
public interface ILifecyclePhaseSource
{
	/// <summary>
	/// Determines whether this source handles the given entity type.
	/// </summary>
	bool Handles(Type entityType);

	/// <summary>
	/// Determines whether the entity has begun.
	/// </summary>
	bool HasBegun(object entity);

	/// <summary>
	/// Determines whether the entity has finished.
	/// </summary>
	bool HasFinished(object entity);
}
