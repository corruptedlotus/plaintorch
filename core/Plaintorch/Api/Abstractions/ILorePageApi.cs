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
}
