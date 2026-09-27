using System.ComponentModel.DataAnnotations;

namespace Pleiades.Orchestration;

/// <summary>
/// Persists the seeking Orbit engine state for an orbit-bearing owner (PEP100): an incentive
/// (<see cref="IncentiveOrbitScheduleState"/>) or a timeframe (<see cref="TimeframeOrbitScheduleState"/>, PEP100
/// patch 2).
/// </summary>
/// <remarks>
/// <para>
/// The state is a self-contained orbit snapshot (notation + epoch + seed + cursor + counters). Seeking resolution
/// (materializing instances for a committed window) advances and re-persists it; preview resolution never touches
/// it. The row is reset whenever its owner's orbit notation changes, and removed when the orbit is cleared. Kept out
/// of the owning entity itself so watcher frontmatter syncs and API payloads never carry or clobber engine state.
/// </para>
/// <para>
/// The kinds share the <c>OrbitScheduleStates</c> table through a discriminator (TPH, PEP100 patch 2); each is keyed
/// uniquely by its owner and cascades away with it.
/// </para>
/// </remarks>
public abstract class OrbitScheduleState
{
	/// <summary>
	/// Gets or sets the database identity of the state row.
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the serialized orbit snapshot JSON.
	/// </summary>
	public required string StateJson { get; set; }

	/// <summary>
	/// Gets or sets when the state was last advanced or reset.
	/// </summary>
	public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
