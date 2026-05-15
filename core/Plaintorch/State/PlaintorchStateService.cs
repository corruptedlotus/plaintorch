using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
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
			.Where(sprint => sprint.Id != "0"
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
			.Where(sprint => sprint.Id == "0")
			.OrderByDescending(sprint => sprint.StartDate)
			.FirstOrDefaultAsync(cancellationToken);
	}

	/// <summary>
	/// Gets the currently active Polaris cycle, when one exists.
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

		var todayId = DateOnly.FromDateTime(DateTime.Today).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
		return await context.PolarisCycles
			.AsNoTracking()
			.FirstOrDefaultAsync(cycle => cycle.Id == todayId, cancellationToken);
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
