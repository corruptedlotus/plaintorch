using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Provides shared state queries used across the PLAINTORCH application API surfaces.
/// </summary>
public sealed class PlaintorchStateService(
	PlainfraContext context,
	PlaintorchVaultActivationService activationService,
	VaultLayout layout)
{
	/// <summary>
	/// Gets the currently active vault path.
	/// </summary>
	/// <returns>The active vault path.</returns>
	public string GetActiveVaultPath()
	{
		return activationService.GetActiveVaultPath() ?? layout.VaultRoot;
	}

	/// <summary>
	/// Gets the currently active onrush sprint, when one exists.
	/// </summary>
	public Task<OnrushSprint?> GetActiveOnrushSprintAsync(CancellationToken cancellationToken = default)
	{
		return context.OnrushSprints
			.AsNoTracking()
			.Where(sprint => sprint.Id != OnrushSprint.PlanningPlaceholderId
				&& sprint.StartDate != null
				&& sprint.EndDate == null)
			.OrderByDescending(sprint => sprint.StartDate)
			.ThenByDescending(sprint => sprint.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}

	/// <summary>
	/// Gets the most relevant planned onrush sprint when no sprint is currently active.
	/// </summary>
	public async Task<OnrushSprint?> GetPlanningOnrushSprintAsync(CancellationToken cancellationToken = default)
	{
		return await context.OnrushSprints
			.AsNoTracking()
			.Where(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId)
			.OrderByDescending(sprint => sprint.StartDate)
			.FirstOrDefaultAsync(cancellationToken);
	}

	/// <summary>
	/// Gets the current Polaris cycle: the running one, else today's cycle while it has not concluded — a forecast
	/// planned for today, which a client shows planned and offers to begin. Today's cycle once concluded is not
	/// current: the day's work is done, and a client showing it would time it as if it still ran.
	/// </summary>
	public async Task<PolarisCycle?> GetActivePolarisCycleAsync(CancellationToken cancellationToken = default)
	{
		var active = await context.PolarisCycles
			.AsNoTracking()
			.Where(cycle => cycle.StartTime != null && cycle.EndTime == null)
			.OrderByDescending(cycle => cycle.Id)
			.FirstOrDefaultAsync(cancellationToken);

		if (active is not null)
		{
			return active;
		}

		// A Polaris cycle id is a Pleiadean date stamp ({D:p}), not Gregorian yyyyMMdd — computing the today id the wrong
		// way silently never matched, so this fallback never found today's cycle.
		var todayId = PuckDateStampCodec.Format(DateOnly.FromDateTime(DateTime.Today), PuckDateStampKind.Pleiadean);
		return await context.PolarisCycles
			.AsNoTracking()
			.FirstOrDefaultAsync(cycle => cycle.Id == todayId && cycle.EndTime == null, cancellationToken);
	}

	/// <summary>
	/// Gets the current banked Celestron total.
	/// </summary>
	public async Task<int> GetCelestronBankedAsync(CancellationToken cancellationToken = default)
	{
		var total = await context.CelestronLedger
			.AsNoTracking()
			.SumAsync(entry => (decimal?)entry.Amount, cancellationToken)
			?? 0m;

		return decimal.ToInt32(total);
	}
}
