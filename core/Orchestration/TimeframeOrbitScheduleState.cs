using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Persists the Orbit engine state of an orbit-scoped timeframe (PEP100 patch 2). One row per timeframe; it is deleted
/// with the timeframe, including when a lunar directive delete cascades its timeframes away.
/// </summary>
/// <remarks>
/// The state pins an epoch and a seed when the orbit is set, so interval orbits (<c>d%2</c>) keep their phase and
/// random orbits (<c>{#n}</c>) stay deterministic across cycles and boots. The epoch is a fixed <c>Z{…}</c> literal's
/// own date, read on the timeframe calendar (the vault default); otherwise it is the earlier of today and the open
/// Polaris cycle's day. Unlike a declarative, which anchors today, this looks back: the state is preview-only, so
/// anchoring back costs no backfill, and an orbit set under an open cycle stays evaluable on that cycle's day. The
/// state is reset only when the orbit changes (never on a calendar change) and removed when the orbit is cleared.
/// Timeframes are only ever previewed against a Polaris cycle's day, so the state is never advanced.
/// </remarks>
public sealed class TimeframeOrbitScheduleState : OrbitScheduleState
{
	/// <summary>
	/// Gets or sets the owning timeframe's database identity.
	/// </summary>
	public long TimeframeId { get; set; }

	/// <summary>
	/// Gets or sets the owning timeframe.
	/// </summary>
	[ForeignKey(nameof(TimeframeId))]
	public Timeframe? Timeframe { get; set; }
}
