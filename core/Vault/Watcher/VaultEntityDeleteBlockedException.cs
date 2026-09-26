using Pleiades.Resources;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Thrown by a file-driven delete that cannot remove its entity because other rows still reference it through a
/// restricting relationship (a directive with subdirectives, an incentive other incentives name as their parent) — the removal the
/// database would refuse. Nothing is written: neither the removal nor a graveyard entry. The status reporter classifies
/// it as the standing <see cref="WatcherOperations.DeleteBlocked"/> status rather than a retryable sync failure, because
/// re-checking the path on a timer cannot succeed until the note returns or the references go.
/// </summary>
public sealed class VaultEntityDeleteBlockedException : InvalidOperationException
{
	/// <summary>
	/// Creates the exception for an entity whose removal is blocked.
	/// </summary>
	/// <param name="entityType">The entity kind the watcher tried to remove (its model name).</param>
	/// <param name="entityId">The entity's identity.</param>
	/// <param name="entityTitle">The entity's title, when known.</param>
	/// <param name="blockers">The restricting relationships that still reference it; never empty.</param>
	public VaultEntityDeleteBlockedException(string entityType, string entityId, string? entityTitle, IReadOnlyList<VaultEntityDeleteBlocker> blockers)
		: base(WatcherMessages.Details.DeleteBlocked(
			entityType,
			entityTitle,
			entityId,
			string.Join(", ", blockers.Select(static blocker => WatcherMessages.Details.DeleteBlocker(blocker.Count, blocker.DependentEntity)))))
	{
		EntityType = entityType;
		EntityId = entityId;
		Blockers = blockers;
	}

	/// <summary>Gets the entity kind the watcher tried to remove.</summary>
	public string EntityType { get; }

	/// <summary>Gets the identity of the entity that was not removed.</summary>
	public string EntityId { get; }

	/// <summary>Gets the restricting relationships that still reference the entity.</summary>
	public IReadOnlyList<VaultEntityDeleteBlocker> Blockers { get; }
}
