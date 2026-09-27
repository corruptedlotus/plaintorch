using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Resolves the timeframe a freshly created Polaris workitem should be affined to through timeframe auto-inclusion
/// (PEP100 patch, availability per PEP100 patch 2).
/// </summary>
/// <remarks>
/// <para>
/// Precedence (<see cref="ResolveAsync"/>): a directive availability first — the nearest directive, walking from the
/// owning incentive's directive (itself first) up its <see cref="Directive.ParentDirectiveId"/> lineage, whose
/// <see cref="Directive.AvailabilityTimeframeId"/> points at an existing
/// <see cref="TimeframeInclusion.Availability"/>-mode timeframe wins — then college auto-inclusion
/// (<see cref="ResolveForCollegeAsync"/>). Availability never inherits through a parent incentive, and the owning
/// lunar directive's status is ignored, as it always has been.
/// </para>
/// <para>
/// Affinity is single-valued — an executive or reflective points at one timeframe — so when several timeframes
/// auto-include the same college the lowest-id match wins deterministically. The resolver is the single place every
/// auto-assignment goes through (objective executives, decree executives, and cycle-begin reflectives), so they stay
/// in step. It seeds at creation only; later changes to a directive's availability or a timeframe's colleges never
/// re-resolve existing workitems.
/// </para>
/// <para>
/// Scoped with its unit of work: the directive lineage and the Availability-mode timeframe ids are read once per scope
/// (and again after any save through that scope), so resolving every reflective at a cycle begin reads them only once.
/// The college fallback (<see cref="ResolveForCollegeAsync"/>) is not memoized: each resolution that finds no
/// availability reads the College-mode timeframes afresh.
/// </para>
/// </remarks>
public sealed class TimeframeAffinityResolver(PlainfraContext context)
{
	private AvailabilityGraph? _availabilityGraph;
	private bool _listeningForSaves;

	/// <summary>
	/// Resolves the auto-affinity of a workitem whose owning incentive belongs to <paramref name="directiveId"/> and
	/// <paramref name="college"/> (PEP100 patch 2): the nearest directive availability, else the college
	/// auto-inclusion, else <see langword="null"/>.
	/// </summary>
	/// <param name="directiveId">The owning incentive's directive, or <see langword="null"/> when it has none.</param>
	/// <param name="college">The owning incentive's college.</param>
	/// <param name="cancellationToken">A token to cancel the lookup.</param>
	public async Task<long?> ResolveAsync(string? directiveId, ObjectiveCollege college, CancellationToken cancellationToken = default)
	{
		return await ResolveForAvailabilityAsync(directiveId, cancellationToken)
			?? await ResolveForCollegeAsync(college, cancellationToken);
	}

	/// <summary>
	/// Finds the id of the timeframe that auto-includes the given college, or <see langword="null"/> when none does.
	/// Only <see cref="TimeframeInclusion.College"/>-mode timeframes are considered; the lowest id wins.
	/// </summary>
	public async Task<long?> ResolveForCollegeAsync(ObjectiveCollege college, CancellationToken cancellationToken = default)
	{
		// The colleges are a JSON list column, not SQL-queryable, so the college-mode timeframes (few per directive)
		// are materialized and the membership test runs in memory.
		var candidates = await context.Timeframes
			.AsNoTracking()
			.Where(timeframe => timeframe.AutoInclusion == TimeframeInclusion.College)
			.OrderBy(timeframe => timeframe.Id)
			.ToListAsync(cancellationToken);

		return candidates
			.Where(timeframe => timeframe.AutoInclusionColleges.Contains(college))
			.Select(timeframe => (long?)timeframe.Id)
			.FirstOrDefault();
	}

	/// <summary>
	/// Walks from <paramref name="directiveId"/> (itself first) up the directive lineage and returns the nearest
	/// availability that still names an Availability-mode timeframe, or <see langword="null"/>.
	/// </summary>
	private async Task<long?> ResolveForAvailabilityAsync(string? directiveId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(directiveId))
		{
			return null;
		}

		var (lineage, availabilityIds) = await LoadAvailabilityGraphAsync(cancellationToken);

		// The vault is hand-editable, so a lineage may loop; the visited set ends the walk at the first repeat.
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var currentId = directiveId;
		while (!string.IsNullOrWhiteSpace(currentId) && visited.Add(currentId) && lineage.TryGetValue(currentId, out var directive))
		{
			if (directive.AvailabilityTimeframeId is { } timeframeId && availabilityIds.Contains(timeframeId))
			{
				return timeframeId;
			}

			currentId = directive.ParentDirectiveId;
		}

		return null;
	}

	/// <summary>
	/// Loads the directive lineage projection and the Availability-mode timeframe ids, memoized for this resolver's
	/// scope so a loop resolving many workitems (cycle-begin reflectives) reads them once. A save through the scope's
	/// context drops the memo, so a resolution after a write in the same unit of work still sees that write.
	/// </summary>
	private async Task<AvailabilityGraph> LoadAvailabilityGraphAsync(CancellationToken cancellationToken)
	{
		if (_availabilityGraph is { } memoized)
		{
			return memoized;
		}

		if (!_listeningForSaves)
		{
			context.SavedChanges += (_, _) => _availabilityGraph = null;
			_listeningForSaves = true;
		}

		// One projection of the whole lineage graph, walked in memory, instead of a query per ancestor level.
		var lineage = await context.Directives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Select(item => new DirectiveLink(item.Id, item.ParentDirectiveId, item.AvailabilityTimeframeId))
			.ToDictionaryAsync(item => item.Id, StringComparer.Ordinal, cancellationToken);

		// Only availabilities that still point at an Availability-mode timeframe count (defence in depth: leaving the
		// mode clears references, but a hand-edited or legacy row must not auto-assign through a non-availability).
		var availabilityIds = (await context.Timeframes
			.AsNoTracking()
			.Where(timeframe => timeframe.AutoInclusion == TimeframeInclusion.Availability)
			.Select(timeframe => timeframe.Id)
			.ToListAsync(cancellationToken))
			.ToHashSet();

		return _availabilityGraph = new AvailabilityGraph(lineage, availabilityIds);
	}

	private sealed record DirectiveLink(string Id, string? ParentDirectiveId, long? AvailabilityTimeframeId);

	private sealed record AvailabilityGraph(IReadOnlyDictionary<string, DirectiveLink> Lineage, IReadOnlySet<long> AvailabilityIds);
}
