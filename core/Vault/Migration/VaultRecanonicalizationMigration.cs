using Microsoft.EntityFrameworkCore;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration;

/// <summary>
/// Base class for vault migrations that upgrade on-disk conventions by re-canonicalising existing entities.
/// </summary>
/// <remarks>
/// The migration loads its target entity types under the source conventions, snapshots each legacy file to the
/// graveyard for rollback, and re-emits each entity under the current conventions (preserving the markdown body) via
/// <see cref="PlaintorchMarkdownStorageService.RecanonicalizeAsync"/>. Loaded entities absent from the database are
/// created, so the same pass also serves as a file-authoritative import.
/// </remarks>
public abstract class VaultRecanonicalizationMigration(
	VaultLoader loader,
	VaultConventionSetFactory conventionSetFactory,
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	VaultAuditLogService auditLogService,
	VaultLayout layout,
	ILogger logger) : IVaultMigration
{
	/// <inheritdoc />
	public abstract int FromVersion { get; }

	/// <inheritdoc />
	public abstract int ToVersion { get; }

	/// <inheritdoc />
	public abstract string Id { get; }

	/// <inheritdoc />
	public abstract string Description { get; }

	/// <summary>
	/// Gets the entity types this migration re-canonicalises.
	/// </summary>
	protected abstract IReadOnlyCollection<Type> TargetTypes { get; }

	/// <summary>
	/// Gets a value indicating whether re-canonicalisation should begin the entity's implicit synchronization boundary.
	/// </summary>
	protected virtual bool BeginBoundary => false;

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var sourceConventions = conventionSetFactory.BuildForVersion(FromVersion);
		var loaded = await loader.LoadAsync(sourceConventions, TargetTypes, cancellationToken);

		var rewritten = 0;
		var archived = 0;
		var imported = 0;
		var conflicts = 0;

		foreach (var item in loaded)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var existing = await context.FindAsync(item.EntityType, [item.Id], cancellationToken);
				object entity;
				if (existing is null)
				{
					context.Add(item.Entity);
					await context.SaveChangesAsync(cancellationToken);
					entity = item.Entity;
					imported++;
				}
				else
				{
					entity = existing;
				}

				if (await TrySnapshotLegacyFileAsync(item, cancellationToken))
				{
					archived++;
				}

				await storageService.RecanonicalizeAsync(entity, item.AbsolutePath, BeginBoundary, cancellationToken);
				rewritten++;

				await auditLogService.WriteAsync(
					"migration",
					"recanonicalize",
					subjectType: item.EntityName,
					subjectId: item.Id,
					subjectTitle: item.Title,
					details: new { migration = Id, FromVersion, ToVersion, item.VaultRelativePath },
					cancellationToken: cancellationToken);
			}
			catch (Exception exception) when (exception is IOException or InvalidOperationException)
			{
				conflicts++;
				logger.LogWarning(
					exception,
					"Vault migration {Migration} could not re-canonicalise {EntityType} '{EntityId}' from '{Path}'.",
					Id,
					item.EntityName,
					item.Id,
					item.VaultRelativePath);

				await auditLogService.WriteAsync(
					"migration",
					"recanonicalize-conflict",
					subjectType: item.EntityName,
					subjectId: item.Id,
					subjectTitle: item.Title,
					details: new { migration = Id, FromVersion, ToVersion, item.VaultRelativePath, error = exception.Message },
					cancellationToken: cancellationToken);
			}
		}

		return new VaultMigrationOutcome(loaded.Count, rewritten, archived, imported, conflicts);
	}

	/// <summary>
	/// Copies a legacy file into a migration-scoped graveyard snapshot before it is rewritten, providing rollback.
	/// </summary>
	private async Task<bool> TrySnapshotLegacyFileAsync(LoadedVaultEntity item, CancellationToken cancellationToken)
	{
		if (!File.Exists(item.AbsolutePath))
		{
			return false;
		}

		var snapshotRoot = Path.Combine(layout.FileGraveyardRoot, $"migration-v{FromVersion}-to-v{ToVersion}");
		var target = Path.Combine(snapshotRoot, item.VaultRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		if (File.Exists(target))
		{
			return true;
		}

		await using var source = File.OpenRead(item.AbsolutePath);
		await using var destination = File.Create(target);
		await source.CopyToAsync(destination, cancellationToken);
		return true;
	}
}
