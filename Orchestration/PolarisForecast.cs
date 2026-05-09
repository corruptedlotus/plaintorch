using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

[Owned]
/// <summary>
/// Represents the forecast-specific data for a planned Polaris cycle.
/// </summary>
public sealed class PolarisForecast
{
	/// <summary>
	/// Gets or sets the reference date the forecast was planned from.
	/// </summary>
	[MarkdownField("forecastReference")]
	public required DateOnly ForecastReference { get; set; }

	/// <summary>
	/// Gets or sets the forecast target marker.
	/// </summary>
	[MarkdownField("forecastTarget")]
	public required string ForecastTarget { get; set; }
}