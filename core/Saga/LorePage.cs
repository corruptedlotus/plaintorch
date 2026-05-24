using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

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
	private static readonly IComparer<LorePage> _narrativeOrderComparer = Comparer<LorePage>.Create(CompareByNarrativeOrder);

	/// <summary>
	/// Gets the comparer used to order lore pages by hierarchical narrative index.
	/// </summary>
	public static IComparer<LorePage> NarrativeOrderComparer => _narrativeOrderComparer;

	/// <summary>
	/// Gets or sets the canonical lore PUCK persisted in frontmatter for path-override reconciliation.
	/// </summary>
	[NotMapped]
	[MarkdownField("puck")]
	public string? Puck
	{
		get => Id;
		set
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				Id = value.Trim();
			}
		}
	}

	/// <summary>
	/// Gets or sets an optional filename identifier override used for the terminal lore segment.
	/// </summary>
	[MarkdownField("overrideIdentifier")]
	public string? OverrideIdentifier { get; set; }

	/// <summary>
	/// Gets or sets the optional beginning date for the lore period.
	/// </summary>
	[MarkdownField("beginning")]
	public DateOnly? Beginning { get; set; }

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
	/// Gets the terminal PUCK segment.
	/// </summary>
	[NotMapped]
	public string TerminalIdentifier => Id.Split('/').Last();

	/// <summary>
	/// Gets the identifier used for terminal folder and file naming.
	/// </summary>
	[NotMapped]
	public string EffectiveIdentifier => string.IsNullOrWhiteSpace(OverrideIdentifier)
		? TerminalIdentifier
		: OverrideIdentifier.Trim();

	/// <summary>
	/// Determines whether the lore page was ongoing during a specific date.
	/// </summary>
	public bool WasOngoingIn(DateOnly date, DateOnly? endingExclusive)
	{
		if (Beginning is null)
		{
			return false;
		}

		if (date < Beginning.Value)
		{
			return false;
		}

		return endingExclusive is null || date < endingExclusive.Value;
	}

	/// <summary>
	/// Determines whether the lore page is ongoing for a given date context.
	/// </summary>
	public bool IsOngoing(DateOnly? date = null, DateOnly? endingExclusive = null)
	{
		var effectiveDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
		return WasOngoingIn(effectiveDate, endingExclusive);
	}

	/// <summary>
	/// Resolves the exclusive ending boundary from the next known sibling beginning.
	/// </summary>
	public static DateOnly? ResolveEndingExclusive(LorePage lorePage, IEnumerable<LorePage> siblings)
	{
		ArgumentNullException.ThrowIfNull(lorePage);
		ArgumentNullException.ThrowIfNull(siblings);

		if (lorePage.Beginning is null)
		{
			return null;
		}

		return siblings
			.Where(item => item.Beginning is not null && item.Beginning.Value > lorePage.Beginning.Value)
			.OrderBy(item => item.Beginning)
			.Select(item => item.Beginning)
			.FirstOrDefault();
	}

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

	private static int CompareByNarrativeOrder(LorePage left, LorePage right)
	{
		ArgumentNullException.ThrowIfNull(left);
		ArgumentNullException.ThrowIfNull(right);

		var comparison = CompareNullableInt(left.Era, right.Era);
		if (comparison != 0)
		{
			return comparison;
		}

		comparison = CompareNullableInt(left.Chapter, right.Chapter);
		if (comparison != 0)
		{
			return comparison;
		}

		comparison = CompareNullableInt(left.Act, right.Act);
		if (comparison != 0)
		{
			return comparison;
		}

		comparison = CompareNullableInt(left.Phase, right.Phase);
		if (comparison != 0)
		{
			return comparison;
		}

		return string.Compare(left.TerminalIdentifier, right.TerminalIdentifier, StringComparison.OrdinalIgnoreCase);
	}

	private static int CompareNullableInt(int? left, int? right)
	{
		if (left.HasValue && right.HasValue)
		{
			return left.Value.CompareTo(right.Value);
		}

		if (left.HasValue)
		{
			return -1;
		}

		if (right.HasValue)
		{
			return 1;
		}

		return 0;
	}
}