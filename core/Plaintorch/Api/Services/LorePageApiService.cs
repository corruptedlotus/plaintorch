using Microsoft.EntityFrameworkCore;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Saga;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the lore page-facing PLAINTORCH application API.
/// </summary>
public sealed class LorePageApiService(PlainfraContext context, PlaintorchMarkdownStorageService markdownStorageService) : ILorePageApi
{
	/// <inheritdoc />
	public async Task<LorePage?> GetAsync(string puck, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);
		var lorePage = await context.LorePages
			.Include(x => x.Parent)
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);

		return lorePage is null ? null : lorePage;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<LorePageRecord>> ListAsync(CancellationToken cancellationToken = default)
	{
		return await context.LorePages
			.AsNoTracking()
			.OrderBy(item => item.Id)
			.Select(item => new LorePageRecord(
				item.Id,
				item.Title,
				item.OverrideIdentifier,
				item.ParentId,
				item.Beginning,
				item.Level,
				item.RelativePath,
				item.Era,
				item.Chapter,
				item.Act,
				item.Phase,
				item.IndexedUtc))
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<LorePageRecord?> UpdateAsync(string puck, LorePageUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);
		ArgumentNullException.ThrowIfNull(update);

		var lorePage = await context.LorePages.FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);
		if (lorePage is null)
		{
			return null;
		}

		if (update.Beginning.IsSet)
		{
			lorePage.Beginning = update.Beginning.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		// Lore is file-first, so the change is written back through the entity's own markdown file (frontmatter only,
		// body preserved). A null previous keeps the existing file in place — only a frontmatter field changed.
		await markdownStorageService.SaveLorePageAsync(lorePage, cancellationToken: cancellationToken);
		return Map(lorePage);
	}

	private static LorePageRecord Map(LorePage lorePage)
	{
		return new LorePageRecord(
			lorePage.Id,
			lorePage.Title,
			lorePage.OverrideIdentifier,
			lorePage.ParentId,
			lorePage.Beginning,
			lorePage.Level,
			lorePage.RelativePath,
			lorePage.Era,
			lorePage.Chapter,
			lorePage.Act,
			lorePage.Phase,
			lorePage.IndexedUtc);
	}
}
