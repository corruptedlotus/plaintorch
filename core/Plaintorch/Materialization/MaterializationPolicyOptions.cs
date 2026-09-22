namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Tunable policy for how declarative occurrences harden into persisted rows (Strategy 1 / soft agenda).
/// </summary>
/// <remarks>
/// This is the seam for a future per-user preference: today it carries the hardcoded default, and later it can
/// be bound from the user's configuration without any call site changing.
/// </remarks>
public sealed class MaterializationPolicyOptions
{
	/// <summary>
	/// Gets or sets a value indicating whether the passage of time counts as an interaction that hardens an
	/// occurrence. When <see langword="true"/> (the default), an occurrence whose time has arrived is persisted
	/// permanently, so history is immutable and a later schedule change cannot recompute the past away. When
	/// <see langword="false"/>, a past occurrence stays a pure projection and recomputes when its schedule
	/// changes; only explicit interactions harden it.
	/// </summary>
	public bool TimeIsInteraction { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether the passage of time hardens an OPTED-OUT fate's occurrences
	/// (PEP100/PEP111). An opted-out fate keeps generating, but its occurrences spawn with
	/// <see cref="Pleiades.Orchestration.EventiveResolution.OptOut"/> and are hidden. When <see langword="false"/>
	/// (the default), the rolling pass never persists them — they stay projections and are only ever hardened by
	/// explicit interaction. When <see langword="true"/>, the pass hardens them like active occurrences, but
	/// stamped <see cref="Pleiades.Orchestration.EventiveResolution.OptOut"/> so they remain hidden.
	/// </summary>
	public bool HardenOptOutOnTimePassage { get; set; }
}
