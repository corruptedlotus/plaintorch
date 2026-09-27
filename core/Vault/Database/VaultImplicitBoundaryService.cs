using Microsoft.EntityFrameworkCore;
using Pleiades.Puck;

namespace Pleiades.Vault.Database;

/// <summary>
/// Tracks the synchronization boundary of <see cref="VaultStorageMode.Implicit"/> entities: whether an entity's note has
/// begun to exist, after which the note's deletion is authoritative.
/// </summary>
/// <remarks>
/// <para>
/// A boundary is an identity only; it records no path. An implicit note, like a freeform one, carries its identity inside
/// the file (its frontmatter PUCK) and may live anywhere, so where a note is — or whether it still exists — is read from
/// the vault by identity (which files assert it), never remembered. A remembered path went stale the moment the note or
/// an ancestor folder moved, and a delete was then read against the wrong place.
/// </para>
/// <para>
/// The boundary is kept as audit transitions: <c>BoundaryBegin</c> once the entity's note first exists, and
/// <c>BoundaryEnd</c> when the entity is deleted — staged in the deleting save itself
/// (<see cref="StageBoundaryEndsAsync"/>), whatever the pathway. The latest transition is the boundary's state. The
/// <see cref="AuditLogEntry.Action"/> column is indexed to keep these checks cheap.
/// </para>
/// </remarks>
public sealed class VaultImplicitBoundaryService(PlainfraContext context, VaultAuditLogService auditLogService)
{
	/// <summary>
	/// The audit category used for synchronization-boundary bookkeeping.
	/// </summary>
	public const string BoundaryCategory = "boundary";

	/// <summary>
	/// The audit action recorded when an implicit entity's note first exists, beginning its boundary.
	/// </summary>
	public const string BoundaryBeginAction = "BoundaryBegin";

	/// <summary>
	/// The audit action recorded when an entity with a standing boundary is deleted, ending its boundary.
	/// </summary>
	public const string BoundaryEndAction = "BoundaryEnd";

	/// <summary>
	/// Determines whether a synchronization boundary stands for an implicit entity: begun, and not ended since.
	/// </summary>
	public async Task<bool> HasBoundaryBegunAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

		var latest = await context.AuditLogEntries
			.AsNoTracking()
			.Where(entry => (entry.Action == BoundaryBeginAction || entry.Action == BoundaryEndAction)
				&& entry.SubjectType == entityType
				&& entry.SubjectId == entityId)
			.OrderByDescending(entry => entry.Id)
			.Select(entry => entry.Action)
			.FirstOrDefaultAsync(cancellationToken);
		return latest == BoundaryBeginAction;
	}

	/// <summary>
	/// Records a <c>BoundaryBegin</c> entry for an implicit entity whose boundary does not stand yet.
	/// </summary>
	/// <param name="entityType">The entity CLR type name.</param>
	/// <param name="entityId">The entity PUCK identity.</param>
	/// <param name="entityTitle">The entity title, when available.</param>
	/// <param name="cancellationToken">A token used to cancel the operation.</param>
	public async Task EnsureBoundaryBegunAsync(
		string entityType,
		string entityId,
		string? entityTitle,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

		if (await HasBoundaryBegunAsync(entityType, entityId, cancellationToken))
		{
			return;
		}

		await auditLogService.WriteAsync(
			BoundaryCategory,
			BoundaryBeginAction,
			subjectType: entityType,
			subjectId: entityId,
			subjectTitle: entityTitle,
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Enumerates the entities whose boundary stands (begun, not ended). The sweep checks each against the notes in the
	/// vault: one whose identity no note asserts any more had its note deleted, which the watcher then reconciles — the
	/// only way to see a deletion that happened while the daemon was off, or whose event named no identity.
	/// </summary>
	public async Task<IReadOnlyList<VaultBoundary>> EnumerateBegunBoundariesAsync(CancellationToken cancellationToken = default)
	{
		var transitions = await context.AuditLogEntries
			.AsNoTracking()
			.Where(entry => entry.Action == BoundaryBeginAction || entry.Action == BoundaryEndAction)
			.OrderBy(entry => entry.Id)
			.Select(entry => new { entry.Action, entry.SubjectType, entry.SubjectId })
			.ToListAsync(cancellationToken);

		var latest = new Dictionary<VaultBoundary, string>();
		foreach (var transition in transitions)
		{
			if (!string.IsNullOrWhiteSpace(transition.SubjectType) && !string.IsNullOrWhiteSpace(transition.SubjectId))
			{
				latest[new VaultBoundary(transition.SubjectType, transition.SubjectId)] = transition.Action;
			}
		}

		return latest
			.Where(static pair => pair.Value == BoundaryBeginAction)
			.Select(static pair => pair.Key)
			.ToList();
	}

	/// <summary>
	/// Stages a <c>BoundaryEnd</c> entry for each deleted entity whose boundary stands, into the caller's pending save, so
	/// the boundary ends atomically with the entity. Called by the save-time state rules for every deleting save.
	/// </summary>
	/// <param name="context">The context whose pending save deletes the entities.</param>
	/// <param name="deleted">The entities the pending save deletes.</param>
	/// <param name="cancellationToken">A token used to cancel the lookup.</param>
	public static async Task StageBoundaryEndsAsync(PlainfraContext context, IReadOnlyCollection<IPuckNamedEntity> deleted, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(deleted);
		var ids = deleted.Select(static entity => entity.Id).Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
		if (ids.Count == 0)
		{
			return;
		}

		var transitions = await context.AuditLogEntries
			.AsNoTracking()
			.Where(entry => (entry.Action == BoundaryBeginAction || entry.Action == BoundaryEndAction)
				&& entry.SubjectId != null
				&& ids.Contains(entry.SubjectId))
			.OrderBy(entry => entry.Id)
			.Select(entry => new { entry.Action, entry.SubjectType, entry.SubjectId })
			.ToListAsync(cancellationToken);

		var latest = new Dictionary<VaultBoundary, string>();
		foreach (var transition in transitions.Where(static transition => !string.IsNullOrWhiteSpace(transition.SubjectType)))
		{
			latest[new VaultBoundary(transition.SubjectType!, transition.SubjectId!)] = transition.Action;
		}

		foreach (var entity in deleted)
		{
			var boundary = new VaultBoundary(entity.GetType().Name, entity.Id);
			if (latest.TryGetValue(boundary, out var action) && action == BoundaryBeginAction)
			{
				context.AuditLogEntries.Add(new AuditLogEntry
				{
					OccurredUtc = DateTimeOffset.UtcNow,
					Category = BoundaryCategory,
					Action = BoundaryEndAction,
					SubjectType = boundary.EntityType,
					SubjectId = boundary.EntityId,
					SubjectTitle = entity.Title,
				});
			}
		}
	}
}

/// <summary>An implicit entity whose synchronization boundary has begun: its type name and identity, never a path.</summary>
public readonly record struct VaultBoundary(string EntityType, string EntityId);
