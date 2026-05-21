using Pleiades.Saga;
using Pleiades.Vault.Database;

namespace Pleiades.Vault;

/// <summary>
/// Scans saga markdown files and maintains a lightweight lore index in the vault database.
/// </summary>
public sealed class LoreIndexer(
	VaultLayout layout,
	PlaintorchRepository repository)
{
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
			.Cast<LorePage>()
			.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
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
			.Cast<LorePage>()
			.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		await repository.ReplaceLoreIndexEntriesAsync(entries, cancellationToken);
		return entries.Length;
	}

	private LorePage? TryCreateEntry(string filePath)
	{
		var lorePage = new LorePage
		{
			Id = string.Empty,
			Title = string.Empty,
		};
		if (!LorePage.TryApplyCompositionFromPath(lorePage, filePath, layout.VaultRoot, layout.SagaRoot))
		{
			return null;
		}

		return lorePage;
	}
}