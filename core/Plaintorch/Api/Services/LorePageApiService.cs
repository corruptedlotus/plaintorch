using Microsoft.EntityFrameworkCore;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Saga;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the lore page-facing PLAINTORCH application API.
/// </summary>
public sealed class LorePageApiService(PlainfraContext context) : ILorePageApi
{
	/// <inheritdoc />
	public async Task<LorePageRecord?> GetAsync(string puck, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);
		var lorePage = await context.LorePages
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);

		return lorePage is null ? null : Map(lorePage);
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
