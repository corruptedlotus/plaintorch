using Pleiades.Puck;

namespace Pleiades.Orchestration;

/// <summary>
/// The due contract (PEP103 patch 7): an item that can carry a deadline — an <see cref="Objective"/>, a
/// <see cref="StellarDirective"/> or a <see cref="Checkpoint"/>. Each one's due spawns its <see cref="Terminative"/>: one
/// occurrence per item, projected while the due acts and hardened like any occurrence, which follows the item's due and
/// lifecycle for as long as it exists, hardened or not.
/// </summary>
/// <remarks>
/// Whether a due acts is the temporal-activation gate's (<see cref="Lifecycle.TemporalActivation.DueActs"/>): it acts while
/// its item is open or started, and an item finished or set aside switches it off. An item whose own due is what sets it
/// aside (<see cref="DueDefers"/>) keeps its due acting until it finishes, or the due would switch itself off the moment it
/// arrived.
/// </remarks>
public interface IDueOwner : IPuckNamedEntity
{
	/// <summary>
	/// Gets the item's deadline, or <see langword="null"/> when it has none (and so spawns no terminative).
	/// </summary>
	Due? Due { get; }

	/// <summary>
	/// Gets whether passing its due is what sets the item aside, as it does a checkpoint still locked
	/// (<c>CheckpointLifecycleStateSource</c>). Such an item's due keeps acting while it is set aside, until it finishes.
	/// </summary>
	bool DueDefers => false;
}
