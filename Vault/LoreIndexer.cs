using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Vault;

/// <summary>
/// Scans saga markdown files and maintains a lightweight lore index in the vault database.
/// </summary>
public sealed class LoreIndexer(
	VaultLayout layout,
	PlaintorchRepository repository,
	PuckTokenizer puckTokenizer,
	PuckSemanticProjector semanticProjector)
{
	private const string LoreNotation = "Era{?}/Chapter{?}/Act{?}/Phase{?}";

	/// <summary>
	/// Rebuilds the lore index from the current saga markdown files.
	/// </summary>
	/// <returns>The number of indexed lore entries.</returns>
	public int RebuildIndex()
	{
		if (!Directory.Exists(layout.SagaRoot))
		{
			repository.ReplaceLoreIndexEntries([]);
			return 0;
		}

		var entries = Directory.EnumerateFiles(layout.SagaRoot, "*.md", SearchOption.AllDirectories)
			.Select(TryCreateEntry)
			.Where(entry => entry is not null)
			.Cast<LoreIndexEntry>()
			.OrderBy(entry => entry.Puck, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		repository.ReplaceLoreIndexEntries(entries);
		return entries.Length;
	}

	/// <summary>
	/// Rebuilds the lore index from the current saga markdown files.
	/// </summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The number of indexed lore entries.</returns>
	public async Task<int> RebuildIndexAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!Directory.Exists(layout.SagaRoot))
		{
			await repository.ReplaceLoreIndexEntriesAsync([], cancellationToken);
			return 0;
		}

		var entries = Directory.EnumerateFiles(layout.SagaRoot, "*.md", SearchOption.AllDirectories)
			.Select(TryCreateEntry)
			.Where(entry => entry is not null)
			.Cast<LoreIndexEntry>()
			.OrderBy(entry => entry.Puck, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		await repository.ReplaceLoreIndexEntriesAsync(entries, cancellationToken);
		return entries.Length;
	}

	private LoreIndexEntry? TryCreateEntry(string filePath)
	{
		var relativePath = Path.GetRelativePath(layout.SagaRoot, filePath);
		var relativeSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (relativeSegments.Length == 0)
		{
			return null;
		}

		var fileName = Path.GetFileNameWithoutExtension(relativeSegments[^1]);
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}

		var pathSegments = relativeSegments[..^1].ToList();
		if (pathSegments.Count == 0 || !string.Equals(pathSegments[^1], fileName, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var puckSegments = pathSegments.Select(PuckNamedIdentity.Parse).ToArray();
		var puck = string.Join('/', puckSegments.Select(segment => segment.Id));
		var title = puckSegments[^1].Title;
		var parentPuck = puckSegments.Length > 1
			? string.Join('/', puckSegments.Take(puckSegments.Length - 1).Select(segment => segment.Id))
			: null;

		var tokenization = puckTokenizer.Tokenize(LoreNotation, puck);
		var projection = semanticProjector.ProjectByDiscriminator(tokenization);
		var terminalToken = tokenization.Segments[^1];

		return new LoreIndexEntry
		{
			Puck = puck,
			Title = title,
			Level = terminalToken.Discriminator ?? "Lore",
			RelativePath = Path.GetRelativePath(layout.VaultRoot, filePath),
			ParentPuck = parentPuck,
			Era = TryGetProjectedInt(projection, "Era"),
			Chapter = TryGetProjectedInt(projection, "Chapter"),
			Act = TryGetProjectedInt(projection, "Act"),
			Phase = TryGetProjectedInt(projection, "Phase"),
			IndexedUtc = DateTimeOffset.UtcNow,
		};
	}

	private static int? TryGetProjectedInt(PuckSemanticProjection projection, string key)
	{
		if (!projection.Values.TryGetValue(key, out var value) || value is null)
		{
			return null;
		}

		return value switch
		{
			int intValue => intValue,
			long longValue when longValue >= int.MinValue && longValue <= int.MaxValue => (int)longValue,
			DateOnly => null,
			string text when int.TryParse(text, out var parsed) => parsed,
			_ => null,
		};
	}
}