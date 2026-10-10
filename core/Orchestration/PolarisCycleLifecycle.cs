using Pleiades.Plaintorch;

namespace Pleiades.Orchestration;

/// <summary>
/// Creates and transitions Polaris cycles through forecast, start, and finish phases.
/// </summary>
public sealed class PolarisCycleLifecycle
{
	/// <summary>
	/// Plans a Polaris forecast: a cycle for a day that is planned now and begun later. The day may be the reference
	/// day itself (a horizon of zero) — today's cycle planned before it is begun, which beginning the day then activates
	/// in place.
	/// </summary>
	/// <param name="forecastReference">The reference date from which the forecast is computed.</param>
	/// <param name="daysAhead">The forecast horizon in days; zero plans the reference day itself.</param>
	/// <param name="id">The centrally issued PUCK identifier for the cycle.</param>
	/// <returns>The planned forecast cycle.</returns>
	public PolarisCycle PlanForecast(DateOnly forecastReference, int daysAhead, string id)
	{
		if (daysAhead < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(daysAhead), "A forecast cannot be planned for a day before its reference.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(id);

		var targetDate = forecastReference.AddDays(daysAhead);
		return new PolarisCycle
		{
			Id = id,
			Title = PlaintorchDefaultTitleFactory.CreatePolarisTitle(targetDate),
			Forecast = new PolarisForecast
			{
				ForecastReference = forecastReference,
				ForecastTarget = $"+{daysAhead}d",
			},
		};
	}

	/// <summary>
	/// Converts a forecast cycle into a started cycle.
	/// </summary>
	public PolarisCycle Start(PolarisCycle cycle, DateTimeOffset startTime)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		return new PolarisCycle
		{
			Id = cycle.Id,
			Title = cycle.Title,
			StartTime = startTime,
			EndTime = cycle.EndTime,
			Forecast = null,
		};
	}

	/// <summary>
	/// Completes a cycle. Whether it may end is the state engine's to say, on every pathway (PEP103 patch 4): a cycle that
	/// has not begun, or has already ended, is refused when the end is saved (<c>polaris.end</c>, <c>polaris.restart</c>).
	/// </summary>
	public PolarisCycle Finish(PolarisCycle cycle, DateTimeOffset endTime)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		return new PolarisCycle
		{
			Id = cycle.Id,
			Title = cycle.Title,
			StartTime = cycle.StartTime,
			EndTime = endTime,
			Forecast = cycle.Forecast,
		};
	}
}