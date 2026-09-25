using System.Text.Json;
using System.Text.Json.Serialization;
using Pleiades.Puck;

namespace Pleiades.Vault.Database;

/// <summary>
/// Archives deleted database entities and file-backed content into temporal graveyards.
/// </summary>
public sealed class VaultTemporalDataService(
	PlainfraContext context,
	VaultLayout layout)
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
	};

	/// <summary>
	/// Stages a deleted entity's snapshot into the database graveyard <em>without saving it</em>. The entry is committed
	/// by the caller's own <c>SaveChanges</c> — the same one that removes the entity — so the archive and the removal
	/// land in one transaction: a removal the database refuses leaves no graveyard entry behind. Archiving used to save
	/// on its own first, so every refused delete still wrote an entry, and a delete retried against a standing refusal
	/// (the watcher re-deleting an objective that still had executive records) wrote one per attempt.
	/// </summary>
	/// <param name="entity">The entity about to be removed; its serialized form is the graveyard payload.</param>
	/// <param name="reason">Why the entity is being removed (for example <c>api-delete</c>, <c>watcher-file-delete</c>).</param>
	/// <param name="archivedBy">Who removed it, when known.</param>
	/// <returns>The staged entry. Its key, type, id, and title are set; its database id is assigned on save.</returns>
	public DatabaseGraveyardEntry StageEntityArchive(object entity, string reason, string? archivedBy = null)
	{
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentException.ThrowIfNullOrWhiteSpace(reason);

		var descriptor = Describe(entity);
		var entry = new DatabaseGraveyardEntry
		{
			EntryKey = CreateEntryKey(),
			EntityType = descriptor.Type,
			EntityId = descriptor.Id,
			EntityTitle = descriptor.Title,
			PayloadJson = JsonSerializer.Serialize(entity, entity.GetType(), SerializerOptions),
			Reason = reason,
			ArchivedBy = archivedBy,
			DeletedUtc = DateTimeOffset.UtcNow,
		};

		context.DatabaseGraveyardEntries.Add(entry);
		return entry;
	}

	/// <summary>
	/// Archives a deleted file or directory into the file graveyard.
	/// </summary>
	public async Task<FileGraveyardEntry?> ArchivePathAsync(
		string path,
		string reason,
		string? entityType = null,
		string? entityId = null,
		string? entityTitle = null,
		string? archivedBy = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentException.ThrowIfNullOrWhiteSpace(reason);

		var fullPath = Path.GetFullPath(path);
		var isDirectory = Directory.Exists(fullPath);
		if (!isDirectory && !File.Exists(fullPath))
		{
			return null;
		}

		Directory.CreateDirectory(layout.FileGraveyardRoot);

		var entryKey = CreateEntryKey();
		var originalRelativePath = ResolveVaultRelativePath(fullPath);
		var containerName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{entryKey}";
		var archiveRoot = Path.Combine(layout.FileGraveyardRoot, containerName);
		var archivedFullPath = Path.Combine(archiveRoot, originalRelativePath);

		Directory.CreateDirectory(Path.GetDirectoryName(archivedFullPath)!);
		if (isDirectory)
		{
			Directory.Move(fullPath, archivedFullPath);
		}
		else
		{
			File.Move(fullPath, archivedFullPath);
		}

		var entry = new FileGraveyardEntry
		{
			EntryKey = entryKey,
			EntityType = entityType,
			EntityId = entityId,
			EntityTitle = entityTitle,
			OriginalRelativePath = originalRelativePath,
			ArchivedRelativePath = Path.GetRelativePath(layout.MetadataRoot, archivedFullPath),
			IsDirectory = isDirectory,
			Reason = reason,
			ArchivedBy = archivedBy,
			DeletedUtc = DateTimeOffset.UtcNow,
		};

		context.FileGraveyardEntries.Add(entry);
		await context.SaveChangesAsync(cancellationToken);
		return entry;
	}

	private static string CreateEntryKey() => Guid.NewGuid().ToString("N");

	private string ResolveVaultRelativePath(string fullPath)
	{
		return Path.GetRelativePath(layout.VaultRoot, fullPath);
	}

	private static (string Type, string? Id, string? Title) Describe(object entity)
	{
		if (entity is IPuckNamedEntity namedEntity)
		{
			return (entity.GetType().Name, namedEntity.Id, namedEntity.Title);
		}

		var type = entity.GetType();
		var id = type.GetProperty("Id")?.GetValue(entity)?.ToString();
		var title = type.GetProperty("Title")?.GetValue(entity)?.ToString();
		return (type.Name, id, title);
	}
}