using Microsoft.EntityFrameworkCore;

namespace Pleiades.Saga;

/// <summary>
/// Caches the ids of the pages on the active lore spine (<see cref="LoreIndex"/>) so any lore page can be
/// stamped active on the fly, without every endpoint recomputing the spine or hand-stamping the flag.
/// </summary>
/// <remarks>
/// The active spine is a global projection over every lore page — a single-page endpoint cannot compute it — so
/// it is resolved once here and reused. Keyed by the vault session generation, so switching vaults recomputes
/// automatically; invalidated on any lore-page write (see <c>LoreActiveCacheInterceptor</c>), so an edit that
/// shifts the spine is reflected on the next read.
/// </remarks>
public sealed class LoreActiveCache
{
	private readonly SemaphoreSlim _lock = new(1, 1);
	private volatile Entry? _entry;

	private sealed record Entry(long Generation, IReadOnlySet<string> ActiveIds);

	/// <summary>
	/// Returns the active spine's page ids, recomputing from <paramref name="pages"/> when the cache is empty,
	/// was invalidated, or the vault session <paramref name="generation"/> has moved on.
	/// </summary>
	public async Task<IReadOnlySet<string>> GetActiveIdsAsync(IQueryable<LorePage> pages, long generation, CancellationToken cancellationToken = default)
	{
		var entry = _entry;
		if (entry is not null && entry.Generation == generation)
		{
			return entry.ActiveIds;
		}

		await _lock.WaitAsync(cancellationToken);
		try
		{
			entry = _entry;
			if (entry is not null && entry.Generation == generation)
			{
				return entry.ActiveIds;
			}

			var index = await pages.AsNoTracking().ToLoreIndexAsync(cancellationToken);
			var ids = index.ActivePages.Select(page => page.Id).ToHashSet(StringComparer.Ordinal);
			_entry = new Entry(generation, ids);
			return ids;
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>Drops the cached spine so the next read recomputes it — called when a lore page is written.</summary>
	public void Invalidate() => _entry = null;
}
