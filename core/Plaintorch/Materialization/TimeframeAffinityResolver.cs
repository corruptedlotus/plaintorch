using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Resolves the timeframe a freshly created Polaris workitem should be affined to through timeframe auto-inclusion
/// (PEP100 patch).
/// </summary>
/// <remarks>
/// Affinity is single-valued — an executive or reflective points at one timeframe — so when several timeframes
/// auto-include the same college the lowest-id match wins deterministically. The resolver is the single place both
/// the executive-planning path and the reflective-materialization path go through, so the two stay in step.
/// </remarks>
public sealed class TimeframeAffinityResolver(PlainfraContext context)
{
	/// <summary>
	/// Finds the id of the timeframe that auto-includes the given college, or <see langword="null"/> when none does.
	/// </summary>
	public async Task<long?> ResolveForCollegeAsync(ObjectiveCollege college, CancellationToken cancellationToken = default)
	{
		return await context.Timeframes
			.AsNoTracking()
			.Where(timeframe => timeframe.AutoInclusion == TimeframeInclusion.College && timeframe.AutoInclusionCollege == college)
			.OrderBy(timeframe => timeframe.Id)
			.Select(timeframe => (long?)timeframe.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}
}
