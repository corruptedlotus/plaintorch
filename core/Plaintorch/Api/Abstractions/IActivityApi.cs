using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the activity-facing application actions exposed by PLAINTORCH: a unified search over the incentive
/// kinds a Polaris cycle accepts — objectives and decrees — mirroring the objective search surface.
/// </summary>
public interface IActivityApi
{
	/// <summary>
	/// Lists activities (objectives and decrees).
	/// </summary>
	Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds activities — objectives and decrees — using a free-text query, just like the objective search.
	/// </summary>
	Task<IReadOnlyList<Activity>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default);
}
