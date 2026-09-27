using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the activity-facing PLAINTORCH application API: a unified read over objectives and decrees, the two
/// incentive kinds a Polaris cycle accepts. It mirrors the objective search (id/title contains, optional take) and
/// merges both kinds into one title-ordered list.
/// </summary>
public sealed class ActivityApiService(PlainfraContext context) : IActivityApi
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default)
	{
		var objectives = await context.Objectives.AsNoTracking().ToListAsync(cancellationToken);
		var decrees = await context.Decrees.AsNoTracking().ToListAsync(cancellationToken);
		return Merge(objectives, decrees, take: null);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Activity>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentException.ThrowIfNullOrWhiteSpace(search.Query);

		// Case-insensitive: SQLite's instr (what Contains translates to) is not, so both sides are lowered.
		var query = search.Query.ToLowerInvariant();
		var objectives = await context.Objectives
			.AsNoTracking()
			.Where(objective => objective.Id.ToLower().Contains(query) || objective.Title.ToLower().Contains(query))
			.ToListAsync(cancellationToken);

		var decrees = await context.Decrees
			.AsNoTracking()
			.Where(decree => decree.Id.ToLower().Contains(query) || decree.Title.ToLower().Contains(query))
			.ToListAsync(cancellationToken);

		return Merge(objectives, decrees, search.Take);
	}

	// The take applies across the merged, title-ordered set so it bounds the combined result the way the caller
	// expects — not each kind independently.
	private static IReadOnlyList<Activity> Merge(IEnumerable<Objective> objectives, IEnumerable<Decree> decrees, int? take)
	{
		var merged = objectives.Select(Activity.ForObjective)
			.Concat(decrees.Select(Activity.ForDecree))
			.OrderBy(activity => activity.Title, StringComparer.OrdinalIgnoreCase)
			.AsEnumerable();

		if (take is > 0)
		{
			merged = merged.Take(take.Value);
		}

		return merged.ToList();
	}
}
