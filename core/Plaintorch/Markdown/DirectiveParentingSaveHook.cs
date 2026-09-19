using Pleiades.Orchestration;

namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// Save hook for everything that lives under a directive — subdirectives and incentives: when a save carries a
/// changed directive parent, the note re-homes at its canonical place under the new parent.
/// </summary>
/// <remarks>
/// On disk the path is the authority for who owns what: the watcher derives a directive's parent, and the owner of an
/// incentive inside a directive's folder, from where the note sits. The identity-driven placement policy keeps a note
/// wherever its author put it, which is right for an ordinary edit — and exactly wrong for a reparenting, where the
/// old location *is* the old parent: left in place, the next watcher pass would read the previous parent back and
/// undo the move. So a parent change is the one edit that overrides "keep the authored location". Returning the
/// canonical path is all it takes — the storage pipeline then moves a self-named directory whole (a directive
/// travels with everything nested beneath it) or rewrites a single note at its new path and removes the old one.
/// An entity with no file yet (an implicit incentive before its boundary is begun) never reaches the hook.
/// </remarks>
public sealed class DirectiveParentingSaveHook : IEntitySaveHook
{
	/// <inheritdoc />
	public bool CanHandle(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return typeof(Directive).IsAssignableFrom(entityType) || typeof(Incentive).IsAssignableFrom(entityType);
	}

	/// <inheritdoc />
	public async Task<string?> ResolveWritePathAsync(EntitySaveContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (context.Previous is null
			|| string.Equals(DirectiveParentOf(context.Entity), DirectiveParentOf(context.Previous), StringComparison.OrdinalIgnoreCase))
		{
			// Not a reparenting — the mode's own placement stands.
			return null;
		}

		return await context.ResolveCanonicalPathAsync(context.Entity, cancellationToken);
	}

	private static string? DirectiveParentOf(object entity) => entity switch
	{
		Directive directive => directive.ParentDirectiveId,
		Incentive incentive => incentive.DirectiveId,
		_ => null
	};
}
