using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an executive order: a special constraint or direction that shapes how an onrush sprint is moved through.
/// </summary>
/// <remarks>
/// The PUCK notation embeds the owning onrush's trimmed numeric part as its manual first segment
/// (<c>Onrush x0180 -&gt; E.O. x180-o01</c>), and the order counter increments per owning onrush.
/// </remarks>
[PuckEntity("executive-order")]
[PuckFormat("x{?}-o{I:2:1}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Onrush,
	Mode = VaultStorageMode.Synced,
	Shape = VaultStorageShape.SingleFile,
	PuckStorage = VaultPuckStorage.Index,
	ParentIdProperty = nameof(OnrushSprintId),
	ParentEntityType = typeof(OnrushSprint),
	PartitionUnder = "ExecutiveOrders",
	RequiresParent = true)]
public sealed class ExecutiveOrder : PuckNamedEntity
{
	/// <summary>
	/// Gets or sets the owning onrush sprint identifier.
	/// </summary>
	public required string OnrushSprintId { get; set; }

	[ForeignKey(nameof(OnrushSprintId))]
	[InverseProperty(nameof(OnrushSprint.ExecutiveOrders))]
	/// <summary>
	/// Gets or sets the owning onrush sprint.
	/// </summary>
	public OnrushSprint? OnrushSprint { get; set; }

	/// <summary>
	/// Gets or sets the short summary describing the order's constraint or direction.
	/// </summary>
	[MarkdownField("summary")]
	public string? Summary { get; set; }

	/// <summary>
	/// Gets or sets the optional date the order becomes effective.
	/// </summary>
	[MarkdownField("effectiveFrom")]
	public DateOnly? EffectiveFrom { get; set; }

	/// <summary>
	/// Gets or sets the optional date the order stops being effective.
	/// </summary>
	[MarkdownField("effectiveUntil")]
	public DateOnly? EffectiveUntil { get; set; }

	/// <summary>
	/// Gets a value indicating whether the order carries no explicit window and is therefore Onrush-bound:
	/// its effective span is its parent onrush's, so it is in effect only while the onrush runs.
	/// </summary>
	[NotMapped]
	public bool IsOnrushBound => EffectiveFrom is null && EffectiveUntil is null;

	/// <summary>
	/// Determines whether the order is in effect on <paramref name="date"/>. Each unset bound falls back to the
	/// owning onrush's window (<see cref="OnrushSprint.StartDate"/>/<see cref="OnrushSprint.EndDate"/>), so a
	/// timeless order is Onrush-bound. Requires <see cref="OnrushSprint"/> to be loaded for the fallback to
	/// resolve; an unloaded onrush leaves the corresponding bound unbounded.
	/// </summary>
	public bool IsEffectiveOn(DateOnly date)
	{
		var from = EffectiveFrom ?? OnrushSprint?.StartDate;
		var until = EffectiveUntil ?? OnrushSprint?.EndDate;
		return (from is null || from <= date) && (until is null || until >= date);
	}

	/// <summary>
	/// Gets a value indicating whether the order is in effect today, resolving its window against the owning
	/// onrush (see <see cref="IsEffectiveOn"/>).
	/// </summary>
	[NotMapped]
	public bool IsActive => IsEffectiveOn(DateOnly.FromDateTime(DateTime.Today));
}
