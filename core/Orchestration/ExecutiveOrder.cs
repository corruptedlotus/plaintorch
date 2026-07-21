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
	PartitionUnder = "ExecutiveOrders")]
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
}
