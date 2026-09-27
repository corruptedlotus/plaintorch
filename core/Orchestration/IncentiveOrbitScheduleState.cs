using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Persists the seeking Orbit engine state of an orbit-bearing declarative — a fate or a decree (PEP100). One row per
/// incentive; it is deleted with the incentive.
/// </summary>
/// <remarks>
/// Reset to a fresh state whenever the declarative's orbit changes (anchored today, or at a fixed <c>Z{…}</c>
/// literal's own date), removed when the orbit is cleared, and created lazily on first resolution for a schedule that
/// has none — the lazy row is what pins the schedule's random seed.
/// </remarks>
public sealed class IncentiveOrbitScheduleState : OrbitScheduleState
{
	/// <summary>
	/// Gets or sets the owning incentive identifier (a fate or decree PUCK).
	/// </summary>
	public required string IncentiveId { get; set; }

	/// <summary>
	/// Gets or sets the owning incentive.
	/// </summary>
	[ForeignKey(nameof(IncentiveId))]
	public Incentive? Incentive { get; set; }
}
