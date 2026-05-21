using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;

namespace Pleiades.Saga;

/// <summary>
/// Represents a saga lore page tracked as a file-first vault entity.
/// </summary>
[PuckFormat("Era{?}/Chapter{?}/Act{?}/Phase{?}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Saga,
	Mode = VaultStorageMode.FileFirst,
	Shape = VaultStorageShape.SelfNamedDirectory,
	ParentIdProperty = nameof(ParentId),
	ParentEntityType = typeof(LorePage))]
[Table("LoreIndexEntries")]
public sealed class LorePage : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the parent lore page PUCK, when one exists.
	/// </summary>
	[Column("ParentPuck")]
	public string? ParentId { get; set; }

	/// <summary>
	/// Gets or sets the terminal lore level discriminator, such as <c>Era</c> or <c>Act</c>.
	/// </summary>
	public string Level { get; set; } = "Lore";

	/// <summary>
	/// Gets or sets the vault-relative markdown path.
	/// </summary>
	public string RelativePath { get; set; } = string.Empty;

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

	/// <summary>
	/// Gets or sets the parent lore navigation.
	/// </summary>
	public LorePage? Parent { get; set; }

	/// <summary>
	/// Applies saga lore composition from a canonical markdown path.
	/// </summary>
	public static bool TryApplyCompositionFromPath(LorePage lorePage, string fullPath, string vaultRoot, string sagaRoot)
	{
		ArgumentNullException.ThrowIfNull(lorePage);
		ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(sagaRoot);

		var normalizedFullPath = Path.GetFullPath(fullPath);
		var normalizedSagaRoot = Path.GetFullPath(sagaRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!normalizedFullPath.StartsWith(normalizedSagaRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!string.Equals(Path.GetExtension(normalizedFullPath), ".md", StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(
				Path.GetFileNameWithoutExtension(normalizedFullPath),
				Path.GetFileName(Path.GetDirectoryName(normalizedFullPath)),
				StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var segments = CollectSelfNamedSegments(normalizedFullPath, normalizedSagaRoot);
		if (segments.Count == 0 || segments.Any(segment => string.IsNullOrWhiteSpace(segment.Id)))
		{
			return false;
		}

		var ids = segments.Select(segment => segment.Id!).ToArray();
		lorePage.Id = string.Join('/', ids);
		lorePage.Title = segments[^1].Title;
		lorePage.ParentId = ids.Length > 1
			? string.Join('/', ids.Take(ids.Length - 1))
			: null;
		lorePage.Level = ExtractLevel(ids[^1]);
		lorePage.RelativePath = Path.GetRelativePath(Path.GetFullPath(vaultRoot), normalizedFullPath);
		lorePage.Era = TryReadNumber(ids, "era");
		lorePage.Chapter = TryReadNumber(ids, "chapter");
		lorePage.Act = TryReadNumber(ids, "act");
		lorePage.Phase = TryReadNumber(ids, "phase");
		lorePage.IndexedUtc = DateTimeOffset.UtcNow;
		return true;
	}

	private static List<(string? Id, string Title)> CollectSelfNamedSegments(string fullPath, string sagaRoot)
	{
		var result = new List<(string? Id, string Title)>();
		var currentDirectory = Path.GetDirectoryName(fullPath);

		while (!string.IsNullOrWhiteSpace(currentDirectory)
			&& !string.Equals(currentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), sagaRoot, StringComparison.OrdinalIgnoreCase))
		{
			var directoryName = Path.GetFileName(currentDirectory);
			var primaryFile = Path.Combine(currentDirectory, $"{directoryName}.md");
			if (!File.Exists(primaryFile))
			{
				return [];
			}

			result.Add(PuckNamedIdentity.ParseLoose(directoryName));
			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		result.Reverse();
		return result;
	}

	private static string ExtractLevel(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return "Lore";
		}

		var letters = new string(id.TakeWhile(character => char.IsLetter(character)).ToArray());
		return string.IsNullOrWhiteSpace(letters) ? "Lore" : letters;
	}

	private static int? TryReadNumber(IEnumerable<string> ids, string discriminator)
	{
		var match = ids
			.Select(id => id.Trim())
			.FirstOrDefault(id => id.StartsWith(discriminator, StringComparison.OrdinalIgnoreCase));

		if (match is null)
		{
			return null;
		}

		var digits = new string(match.SkipWhile(character => !char.IsDigit(character)).ToArray());
		return int.TryParse(digits, out var parsed) ? parsed : null;
	}
}