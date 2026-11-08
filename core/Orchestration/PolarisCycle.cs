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

	[NotMapped]
	/// <summary>
	/// Gets the calendar day the cycle stands for: a planned cycle's forecast target, a begun one's start day. The id of
	/// a cycle IS that day's Pleiadean date stamp (<c>{D:p}</c>), so the day is read back off the id rather than derived
	/// again from the times. <see langword="null"/> for an id that is not a date stamp.
	/// </summary>
	public DateOnly? Day => TryParseDay(Id, out var day) ? day : null;

	/// <summary>
	/// Reads the calendar day out of a Polaris cycle id (a Pleiadean date stamp).
	/// </summary>
	/// <param name="cycleId">The cycle id.</param>
	/// <param name="day">The day the id stands for, when it is a date stamp.</param>
	/// <returns>Whether the id is a date stamp.</returns>
	public static bool TryParseDay(string? cycleId, out DateOnly day)
	{
		if (!string.IsNullOrWhiteSpace(cycleId))
		{
			try
			{
				day = PuckDateStampCodec.Parse(cycleId, PuckDateStampKind.Pleiadean);
				return true;
			}
			catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
			{
			}
		}

		day = default;
		return false;
	}

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