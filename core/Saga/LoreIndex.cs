using Mapster;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Saga;

public class IndexedLorePage : LorePage
{
	public bool IsActive { get; set; } = false;
	public IReadOnlyList<IndexedLorePage>? Children { get; set; } = null;

	public IndexedLorePage WithChildrenFrom(List<IndexedLorePage> pool)
	{
		Children = pool
			.Select(p => p.WithChildrenFrom(pool))
			.Where(p => p.ParentId == Id)
			.OrderBy(p => p.Phase ?? p.Act ?? p.Chapter ?? p.Era)
			.ToList();

		return this;
	}
}

public class LoreIndex : List<IndexedLorePage>
{
	public IReadOnlyList<IndexedLorePage> ActivePages { get; protected set; } = null!;

	public LoreIndex(IEnumerable<LorePage> pages, DateTime? asIn = default)
	{
		AddRange(pages.Select(p => p.Adapt<IndexedLorePage>()));
		MarkActivePages(asIn);
	}

	protected void MarkActivePages(DateTime? asIn = default)
	{
		var now = asIn ?? DateTime.UtcNow;
		var latest = this
			.Where(p => p.Beginning is not null && p.Beginning <= DateOnly.FromDateTime(now))
			.OrderByDescending(p => p.Beginning)
			.ThenByDescending(p => LorePage.GetLevelNumber(p.Level))
			.FirstOrDefault();

		List<IndexedLorePage> activePages = [];

		while (latest is not null)
		{
			activePages.Add(latest);
			latest.IsActive = true;
			latest = this.FirstOrDefault(p => p.Id == latest.ParentId);
		}

		ActivePages = activePages;
	}

	public IReadOnlyList<IndexedLorePage> AsStructuredIndex()
	{
		return this
			.Where(p => p.ParentId is null)
			.OrderBy(p => p.Beginning ?? DateOnly.MaxValue)
			.ThenBy(p => p.Era ?? int.MaxValue)
			.Select(p => p.WithChildrenFrom(this))
			.ToList();
	}
}

public static class LoreIndexExtensions
{
	extension(IQueryable<LorePage> query)
	{
		public async Task<LoreIndex> ToLoreIndexAsync(CancellationToken cancellationToken = default, DateTime? asIn = default)
		{
			var list = await query.ToListAsync(cancellationToken);
			var loreIndex = new LoreIndex(list, asIn);
			return loreIndex;
		}
	}
}