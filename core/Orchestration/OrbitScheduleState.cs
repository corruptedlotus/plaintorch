using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Persists the seeking Orbit engine state for an orbit-bearing declarative (PEP100).
/// </summary>
/// <remarks>
/// The state is a self-contained orbit snapshot (notation + epoch + seed + cursor + counters).
/// Seeking resolution (materializing instances for a committed window) advances and re-persists it;
/// preview resolution never touches it. The row is reset whenever the declarative's orbit notation
/// changes, and removed when the orbit is cleared. Kept out of the incentive entity itself so watcher
/// frontmatter syncs and API payloads never carry or clobber engine state.
/// </remarks>
public sealed class OrbitScheduleState
{
	[Key]
	/// <summary>
	/// Gets or sets the owning incentive identifier (a fate or decree PUCK).
	/// </summary>
	public required string IncentiveId { get; set; }

	[ForeignKey(nameof(IncentiveId))]
	/// <summary>
	/// Gets or sets the owning incentive.
	/// </summary>
	public Incentive? Incentive { get; set; }

	/// <summary>
	/// Gets or sets the serialized orbit snapshot JSON.
	/// </summary>
	public required string StateJson { get; set; }

	/// <summary>
	/// Gets or sets when the state was last advanced or reset.
	/// </summary>
	public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
