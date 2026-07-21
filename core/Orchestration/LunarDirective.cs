using Pleiades.Puck;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a Moonlight (lunar) directive: an "everglow" core directive that never starts or ends,
/// acting as a law, routine, or requirement of Project Moonlight (PEP100).
/// </summary>
/// <remarks>
/// Lunar directives live as table siblings to stellar <see cref="Directive"/> records through a discriminator.
/// They ignore the stellar lifecycle <see cref="Directive.Status"/> and carry their own
/// <see cref="LunarStatus"/> instead. Vault storage metadata is inherited from <see cref="Directive"/>.
/// </remarks>
[PuckEntity("lunar-directive")]
[PuckFormat("LUNA{S:3}")]
public sealed class LunarDirective : Directive
{
	/// <summary>
	/// Gets or sets the moonlight state of the lunar directive.
	/// </summary>
	[MarkdownField("lunarStatus")]
	public LunarDirectiveStatus LunarStatus { get; set; } = LunarDirectiveStatus.OnHold;
}
