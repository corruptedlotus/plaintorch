namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// An opt-in, per-type save-time hook. A type whose canonical markdown location needs custom shaping — a lore page
/// re-homing its file under a reassigned parent, say — implements this instead of the central storage service
/// branching on the type. A type with no registered hook uses the default resolution unchanged; register a hook to
/// opt a type in.
/// </summary>
public interface IEntitySaveHook
{
	/// <summary>Whether this hook handles the given entity type.</summary>
	bool CanHandle(Type entityType);

	/// <summary>
	/// Returns an override for the resolved write path, or <see langword="null"/> to keep it. Runs after the mode
	/// policy has resolved the default path, with the previous state (on an update) so the hook can react to a change
	/// such as a reparenting. May mutate the entity (e.g. update a stored relative path).
	/// </summary>
	Task<string?> ResolveWritePathAsync(EntitySaveContext context, CancellationToken cancellationToken);
}

/// <summary>
/// The state a save-time hook sees: the entity being saved, its previous state when this is an update, the default
/// write path the pipeline resolved, and a resolver for an entity's canonical path — so a hook can locate a related
/// entity (e.g. a lore page's parent) without duplicating the storage service's parent-hierarchy composition.
/// </summary>
public sealed record EntitySaveContext(
	object Entity,
	object? Previous,
	string DefaultPath,
	Func<object, CancellationToken, Task<string>> ResolveCanonicalPathAsync);

/// <summary>
/// Typed base for an <see cref="IEntitySaveHook"/> handling entities of type <typeparamref name="T"/>: it dispatches
/// the non-generic surface to a typed override, so implementers work in terms of their own entity.
/// </summary>
public abstract class EntitySaveHook<T> : IEntitySaveHook
	where T : class
{
	/// <inheritdoc />
	public bool CanHandle(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return typeof(T).IsAssignableFrom(entityType);
	}

	/// <inheritdoc />
	public Task<string?> ResolveWritePathAsync(EntitySaveContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.Entity is T entity
			? ResolveWritePathAsync(entity, context.Previous as T, context, cancellationToken)
			: Task.FromResult<string?>(null);
	}

	/// <summary>Return a write-path override for <paramref name="entity"/>, or <see langword="null"/> to keep the default.</summary>
	protected abstract Task<string?> ResolveWritePathAsync(T entity, T? previous, EntitySaveContext context, CancellationToken cancellationToken);
}
