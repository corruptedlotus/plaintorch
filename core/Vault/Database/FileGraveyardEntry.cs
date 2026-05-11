using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Stores metadata for file or directory snapshots preserved in the file graveyard.
/// </summary>
[Index(nameof(EntryKey), IsUnique = true)]
public sealed class FileGraveyardEntry
{
	/// <summary>
	/// Gets or sets the database identity of the graveyard entry.
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the stable public key of the graveyard entry.
	/// </summary>
	public required string EntryKey { get; set; }

	/// <summary>
	/// Gets or sets the related entity type, when known.
	/// </summary>
	public string? EntityType { get; set; }

	/// <summary>
	/// Gets or sets the related entity identifier, when known.
	/// </summary>
	public string? EntityId { get; set; }

	/// <summary>
	/// Gets or sets the related entity title, when known.
	/// </summary>
	public string? EntityTitle { get; set; }

	/// <summary>
	/// Gets or sets the vault-relative original path.
	/// </summary>
	public required string OriginalRelativePath { get; set; }

	/// <summary>
	/// Gets or sets the metadata-root-relative archived path.
	/// </summary>
	public required string ArchivedRelativePath { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the archived path is a directory snapshot.
	/// </summary>
	public bool IsDirectory { get; set; }

	/// <summary>
	/// Gets or sets the archival reason.
	/// </summary>
	public required string Reason { get; set; }

	/// <summary>
	/// Gets or sets the user identity that triggered archival, when known.
	/// </summary>
	public string? ArchivedBy { get; set; }

	/// <summary>
	/// Gets or sets the deletion timestamp.
	/// </summary>
	public DateTimeOffset DeletedUtc { get; set; }
}