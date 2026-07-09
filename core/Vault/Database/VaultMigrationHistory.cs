using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Records each applied vault migration, complementing the per-file audit-log and graveyard provenance records.
/// </summary>
[Index(nameof(ToVersion))]
public sealed class VaultMigrationHistory
{
	/// <summary>
	/// Gets or sets the database identity of the history row.
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the stable migration identifier.
	/// </summary>
	public required string MigrationId { get; set; }

	/// <summary>
	/// Gets or sets the vault schema version the migration read from.
	/// </summary>
	public int FromVersion { get; set; }

	/// <summary>
	/// Gets or sets the vault schema version the migration advanced to.
	/// </summary>
	public int ToVersion { get; set; }

	/// <summary>
	/// Gets or sets the moment the migration was applied.
	/// </summary>
	public DateTimeOffset AppliedUtc { get; set; }

	/// <summary>
	/// Gets or sets the actor that applied the migration.
	/// </summary>
	public string? AppliedBy { get; set; }

	/// <summary>
	/// Gets or sets the wall-clock duration of the migration in milliseconds.
	/// </summary>
	public long DurationMs { get; set; }

	/// <summary>
	/// Gets or sets the number of entities loaded from disk under the source conventions.
	/// </summary>
	public int EntitiesLoaded { get; set; }

	/// <summary>
	/// Gets or sets the number of entities re-canonicalised to current conventions.
	/// </summary>
	public int FilesRewritten { get; set; }

	/// <summary>
	/// Gets or sets the number of legacy files snapshotted to the graveyard.
	/// </summary>
	public int FilesArchived { get; set; }

	/// <summary>
	/// Gets or sets the number of loaded entities that were created because they were absent from the database.
	/// </summary>
	public int EntitiesImported { get; set; }

	/// <summary>
	/// Gets or sets the number of entities that required manual attention.
	/// </summary>
	public int Conflicts { get; set; }

	/// <summary>
	/// Gets or sets the migration outcome status.
	/// </summary>
	public required string Outcome { get; set; }
}
