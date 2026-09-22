using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a Polaris planning or execution cycle for a single day.
/// </summary>
[PuckEntity("polaris-cycle")]
[PuckFormat("{D:p}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Journal,
	Shape = VaultStorageShape.SingleFile,
	Mode = VaultStorageMode.Enforced,
	PuckStorage = VaultPuckStorage.Index)]
public sealed class PolarisCycle : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the forecast information for a planned cycle.
	/// </summary>
	[MarkdownField("forecast")]
	public PolarisForecast? Forecast { get; set; }

	/// <summary>
	/// Gets or sets the actual cycle start time.
	/// </summary>
	[MarkdownField("startTime")]
	public DateTimeOffset? StartTime { get; set; }

	/// <summary>
	/// Gets or sets the actual cycle end time.
	/// </summary>
	[MarkdownField("endTime")]
	public DateTimeOffset? EndTime { get; set; }

	[NotMapped]
	/// <summary>
	/// Gets a value indicating whether the cycle is still only a forecast.
	/// </summary>
	public bool IsForecast => Forecast is not null && StartTime is null;

	[InverseProperty(nameof(Executive.PolarisCycle))]
	/// <summary>
	/// Gets the execution records belonging to the cycle.
	/// </summary>
	public List<Executive> Executives { get; set; } = [];

	[InverseProperty(nameof(Reflective.PolarisCycle))]
	/// <summary>
	/// Gets the reflective records belonging to the cycle.
	/// </summary>
	public List<Reflective> Reflectives { get; set; } = [];
}