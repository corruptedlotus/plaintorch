using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a Moonlight (lunar) directive: an "everglow" core directive that never starts or ends,
/// acting as a law, routine, or requirement of Project Moonlight (PEP100).
/// </summary>
/// <remarks>
/// Lunar directives live as table siblings to stellar <see cref="StellarDirective"/> records through a
/// discriminator, but keep their own dedicated vault storage location (<see cref="VaultLocationKeys.Moonlight"/>,
/// defaulting to <c>./Moonlight</c>) instead of inheriting the stellar <see cref="VaultLocationKeys.Directives"/>
/// root. They carry their own moonlight <see cref="Status"/> and define <see cref="Timeframes"/>.
/// </remarks>
[PuckEntity("lunar-directive")]
[PuckFormat("LUNA{S:3}")]
[VaultStorage(
	LocationKey = VaultLocationKeys.Moonlight,
	Mode = VaultStorageMode.Freeform,
	Shape = VaultStorageShape.SelfNamedDirectory,
	ParentIdProperty = nameof(ParentDirectiveId),
	ParentEntityType = typeof(Directive))]
public sealed class LunarDirective : Directive
{
	/// <summary>
	/// Gets or sets the moonlight state of the lunar directive.
	/// </summary>
	[MarkdownField("status")]
	public LunarDirectiveStatus Status { get; set; } = LunarDirectiveStatus.OnHold;

	[InverseProperty(nameof(Timeframe.Directive))]
	/// <summary>
	/// Gets the timeframes defined by this directive.
	/// </summary>
	public List<Timeframe> Timeframes { get; set; } = [];
}
