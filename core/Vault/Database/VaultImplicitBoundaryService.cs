using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Tracks the synchronization boundary of <see cref="VaultStorageMode.Implicit"/> entities through
/// <c>BoundaryBegin</c> audit log entries.
/// </summary>
/// <remarks>
/// An implicit entity does not initially materialize a file. Once its file begins existing through any means, a
/// <c>BoundaryBegin</c> entry is recorded once; from that point onward deletions of the file are treated as authoritative
/// and reflected upstream. The <see cref="AuditLogEntry.Action"/> column is indexed to keep these rapid boundary checks cheap.
/// </remarks>
public sealed class VaultImplicitBoundaryService(PlainfraContext context, VaultAuditLogService auditLogService)
{
	/// <summary>
	/// The audit category used for synchronization-boundary bookkeeping.
	/// </summary>
	public const string BoundaryCategory = "boundary";

	/// <summary>
	/// The audit action recorded when an implicit entity's file boundary begins.
	/// </summary>
	public const string BoundaryBeginAction = "BoundaryBegin";

	/// <summary>
	/// Determines whether a synchronization boundary has already begun for a specific implicit entity.
	/// </summary>
	public Task<bool> HasBoundaryBegunAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

		return context.AuditLogEntries
			.AsNoTracking()
			.AnyAsync(
				entry => entry.Action == BoundaryBeginAction
					&& entry.SubjectType == entityType
					&& entry.SubjectId == entityId,
				cancellationToken);
	}

	/// <summary>
	/// Records a <c>BoundaryBegin</c> entry for an implicit entity when one does not already exist.
	/// </summary>
	/// <param name="entityType">The entity CLR type name.</param>
	/// <param name="entityId">The entity PUCK identity.</param>
	/// <param name="entityTitle">The entity title, when available.</param>
	/// <param name="vaultRelativePath">The vault-relative markdown path whose existence begins the boundary.</param>
	/// <param name="cancellationToken">A token used to cancel the operation.</param>
	public async Task EnsureBoundaryBegunAsync(
		string entityType,
		string entityId,
		string? entityTitle,
		string? vaultRelativePath,
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
			temporalLocation: vaultRelativePath,
			details: new { vaultRelativePath },
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Attempts to recover an implicit entity identity from the boundary entry recorded for a specific markdown path.
	/// This lets deletions of quiet (title-only) files remain authoritative even though the filename carries no identity.
	/// </summary>
	/// <param name="entityType">The entity CLR type name.</param>
	/// <param name="vaultRelativePath">The vault-relative markdown path being reconciled.</param>
	/// <param name="cancellationToken">A token used to cancel the lookup.</param>
	/// <returns>The recovered entity identity, or <see langword="null"/> when no boundary entry matches the path.</returns>
	public async Task<string?> TryRecoverEntityIdByLocationAsync(
		string entityType,
		string vaultRelativePath,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		if (string.IsNullOrWhiteSpace(vaultRelativePath))
		{
			return null;
		}

		return await context.AuditLogEntries
			.AsNoTracking()
			.Where(entry => entry.Action == BoundaryBeginAction
				&& entry.SubjectType == entityType
				&& entry.TemporalLocation == vaultRelativePath)
			.OrderByDescending(entry => entry.Id)
			.Select(entry => entry.SubjectId)
			.FirstOrDefaultAsync(cancellationToken);
	}

	/// <summary>
	/// Enumerates the entities that have begun a synchronization boundary and the vault-relative path each boundary
	/// was begun at. The startup sweep uses this to reconcile a boundary-begun file that vanished while the daemon was
	/// off — a change a file-driven scan cannot see — so startup reaches the same state a live deletion would have.
	/// </summary>
	public async Task<IReadOnlyList<VaultBoundaryLocation>> EnumerateBegunBoundariesAsync(CancellationToken cancellationToken = default)
	{
		var entries = await context.AuditLogEntries
			.AsNoTracking()
			.Where(entry => entry.Action == BoundaryBeginAction && entry.TemporalLocation != null)
			.Select(entry => new { entry.SubjectType, entry.SubjectId, entry.TemporalLocation })
			.ToListAsync(cancellationToken);

		return entries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.SubjectType)
				&& !string.IsNullOrWhiteSpace(entry.SubjectId)
				&& !string.IsNullOrWhiteSpace(entry.TemporalLocation))
			.Select(entry => new VaultBoundaryLocation(entry.SubjectType!, entry.SubjectId!, entry.TemporalLocation!))
			.DistinctBy(entry => (entry.EntityType, entry.EntityId))
			.ToList();
	}
}

/// <summary>An entity that began a synchronization boundary and the vault-relative path it began at.</summary>
public readonly record struct VaultBoundaryLocation(string EntityType, string EntityId, string VaultRelativePath);
