using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Stores temporal snapshots of deleted database entities without enforcing relational continuity.
/// </summary>
[Index(nameof(EntryKey), IsUnique = true)]
public sealed class DatabaseGraveyardEntry
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
	/// Gets or sets the deleted entity type name.
	/// </summary>
	public required string EntityType { get; set; }

	/// <summary>
	/// Gets or sets the deleted entity identifier.
	/// </summary>
	public string? EntityId { get; set; }

	/// <summary>
	/// Gets or sets the deleted entity title.
	/// </summary>
	public string? EntityTitle { get; set; }

	/// <summary>
	/// Gets or sets the serialized entity payload.
	/// </summary>
	public required string PayloadJson { get; set; }

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