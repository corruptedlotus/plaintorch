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
	/// Archives a deleted entity into the database graveyard.
	/// </summary>
	public async Task<DatabaseGraveyardEntry> ArchiveEntityAsync(object entity, string reason, string? archivedBy = null, CancellationToken cancellationToken = default)
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
		await context.SaveChangesAsync(cancellationToken);
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