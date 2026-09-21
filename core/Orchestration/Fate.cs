using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents the fate declarative (PEP100): an event that cannot be done or undone, but only happen.
/// Fates can be missed or cancelled, be all-day or carry a start and end time (ref. CalDAV vEVENT),
/// and can schedule themselves through the Orbit notation.
/// </summary>
[PuckEntity("fate")]
[PuckFormat("e{S:8}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Fates,
	Mode = VaultStorageMode.Implicit,
	Shape = VaultStorageShape.SingleFile,
	ParentIdProperty = nameof(DirectiveId),
	ParentEntityType = typeof(Directive),
	PartitionUnder = "Fates")]
public sealed class Fate : Declarative
{
	/// <summary>
	/// Gets or sets the current fate state.
	/// </summary>
	[MarkdownField("status")]
	[LifecycleStatus]
	public FateStatus Status { get; set; } = FateStatus.Active;

	/// <summary>
	/// Gets or sets the optional Orbit scheduling notation. Orbits always resolve to day granularity.
	/// </summary>
	[MarkdownField("orbit")]
	public string? Orbit { get; set; }

	/// <summary>
	/// Gets or sets the optional one-off occurrence date for fates that are not orbit-scheduled.
	/// </summary>
	[MarkdownField("date")]
	public DateOnly? Date { get; set; }

	/// <summary>
	/// Gets or sets the optional start time of the event. A fate without a start time is all-day.
	/// </summary>
	[MarkdownField("startTime")]
	public TimeOnly? StartTime { get; set; }

	/// <summary>
	/// Gets or sets the optional end time of the event.
	/// </summary>
	[MarkdownField("endTime")]
	public TimeOnly? EndTime { get; set; }

	/// <summary>
	/// Gets or sets the optional explicit event duration in whole minutes.
	/// This is what fills the eventives the fate materializes; when absent, the duration falls back to
	/// the start/end time window.
	/// </summary>
	[MarkdownField("eventDuration")]
	public int? EventDuration { get; set; }

	[NotMapped]
	/// <summary>
	/// Gets a value indicating whether the fate is an all-day event.
	/// </summary>
	public bool IsAllDay => StartTime is null;

	/// <summary>
	/// Gets the eventives materialized from this fate (PEP100).
	/// </summary>
	[InverseProperty(nameof(Eventive.Fate))]
	public List<Eventive> Eventives { get; set; } = [];

	/// <summary>
	/// Resolves the effective eventive duration in whole minutes, preferring the explicit event duration
	/// and falling back to the start/end window when both times are present.
	/// </summary>
	public int? ResolveEventiveDuration()
	{
		if (EventDuration is not null)
		{
			return EventDuration;
		}

		if (StartTime is null || EndTime is null)
		{
			return null;
		}

		var window = EndTime.Value - StartTime.Value;
		return window < TimeSpan.Zero ? null : (int)window.TotalMinutes;
	}
}
