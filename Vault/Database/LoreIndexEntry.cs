using System.ComponentModel.DataAnnotations;

namespace Pleiades.Vault.Database;

/// <summary>
/// Represents a lightweight database index entry for a file-first lore document.
/// </summary>
public sealed class LoreIndexEntry
{
	/// <summary>
	/// Gets or sets the canonical PUCK for the lore file.
	/// </summary>
	[Key]
	public required string Puck { get; set; }

	/// <summary>
	/// Gets or sets the human-readable lore title.
	/// </summary>
	public required string Title { get; set; }

	/// <summary>
	/// Gets or sets the terminal lore level discriminator, such as <c>Era</c> or <c>Act</c>.
	/// </summary>
	public required string Level { get; set; }

	/// <summary>
	/// Gets or sets the vault-relative markdown path.
	/// </summary>
	public required string RelativePath { get; set; }

	/// <summary>
	/// Gets or sets the parent lore PUCK, when applicable.
	/// </summary>
	public string? ParentPuck { get; set; }

	/// <summary>
	/// Gets or sets the indexed era number.
	/// </summary>
	public int? Era { get; set; }

	/// <summary>
	/// Gets or sets the indexed chapter number.
	/// </summary>
	public int? Chapter { get; set; }

	/// <summary>
	/// Gets or sets the indexed act number.
	/// </summary>
	public int? Act { get; set; }

	/// <summary>
	/// Gets or sets the indexed phase number.
	/// </summary>
	public int? Phase { get; set; }

	/// <summary>
	/// Gets or sets the UTC timestamp of the last successful indexing pass.
	/// </summary>
	public DateTimeOffset IndexedUtc { get; set; }
}