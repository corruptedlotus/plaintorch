using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Saga;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the lore page-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface ILorePageApi
{
	/// <summary>
	/// Gets a lore page by PUCK.
	/// </summary>
	Task<LorePage?> GetAsync(string puck, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists all lore pages in hierarchy order.
	/// </summary>
	Task<IReadOnlyList<LorePageRecord>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a lore page beneath the requested parent (or a new Era when none is given), composing its level, narrative
	/// index, and PUCK identity, and materializing its self-named folder and file. Returns the created record.
	/// </summary>
	Task<LorePageRecord> CreateAsync(LorePageCreateRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an in-place edit to a lore page's title and/or beginning date, writing the change through to its file
	/// (a title change renames the self-named folder). Returns the updated record, or <see langword="null"/> when no
	/// lore page has the given PUCK.
	/// </summary>
	Task<LorePageRecord?> UpdateAsync(string puck, LorePageUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Renumbers a lore page to a new index at its own level, re-keying its PUCK terminal token and cascading the id
	/// change through every descendant (whose ids are prefixed by it), moving the folder subtree. Returns the updated
	/// record, <see langword="null"/> when no lore page has the given PUCK, and throws when the target index is taken.
	/// </summary>
	Task<LorePageRecord?> SetIndexAsync(string puck, int index, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a lore page and its file. Fails when the page still has child lore pages. Returns <see langword="false"/>
	/// when no lore page has the given PUCK.
	/// </summary>
	Task<bool> DeleteAsync(string puck, CancellationToken cancellationToken = default);
}
