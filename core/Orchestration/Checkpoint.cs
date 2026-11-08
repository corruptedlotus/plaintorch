using Pleiades.Puck;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a checkpoint: a simple point in the journey that only aggregates dependencies (PEP101). Once all of its
/// incoming dependencies are met it <em>unlocks</em> (unblocking its dependants), unless it still owes a Celestron toll
/// or an external condition.
/// </summary>
/// <remarks>
/// Checkpoints are PUCK-addressable but database-only (no <c>VaultStorage</c>): they carry no markdown file.
/// A checkpoint is one-shot: it cannot be started, only finished, and its unlock is its finish
/// (<c>CheckpointLifecyclePhaseSource</c>); re-locking un-finishes it. With that single phase implied, a checkpoint
/// endpoint always leaves the dependency's trigger (as source) or constraint (as target) empty. Its due spawns its
/// <see cref="Terminative"/> (<see cref="IDueOwner"/>, PEP103 patch 7), which keeps acting past the due, while the
/// checkpoint stays locked, since passing the due is what sets it aside.
/// </remarks>
[PuckEntity("checkpoint")]
[PuckFormat("c{S:4}")]
public sealed class Checkpoint : PuckNamedEntity, IDueOwner
{
	/// <summary>
	/// Gets or sets the optional deadline moment (PEP111). When set, the Celestron toll is suppressed until the due
	/// arrives — the checkpoint can unlock without paying while there is still time — and only owed once the due
	/// moment has passed. <see langword="null"/> means the toll (if any) is always owed. Owned by the checkpoint.
	/// </summary>
	public Due? Due { get; set; }

	/// <inheritdoc />
	/// <remarks>A checkpoint still locked once its due has passed is deferred by that alone.</remarks>
	bool IDueOwner.DueDefers => true;

	/// <summary>
	/// Gets or sets the optional Celestron toll that must be paid before the checkpoint can unlock. A
	/// <see langword="null"/> value means the checkpoint has no toll.
	/// </summary>
	public int? CelestronToll { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the toll has been paid. Payment is order-independent — it may
	/// happen before or after the dependencies are met.
	/// </summary>
	public bool TollPaid { get; set; }

	/// <summary>
	/// Gets or sets the external condition switch. <see langword="null"/> means no external condition is
	/// required; <see langword="false"/> means required but not yet met; <see langword="true"/> means met.
	/// </summary>
	public bool? ExternalCondition { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the checkpoint is unlocked. Maintained by the dependency
	/// reconciler; it is the emitted unlock signal that satisfies dependencies whose source is this checkpoint.
	/// </summary>
	public bool Unlocked { get; set; }

	/// <summary>
	/// Gets or sets the onrush sprint that tracks this checkpoint, if any (PEP102). An onrush tracks
	/// checkpoints the way it tracks objectives; a sprint's milestone is one of them, singled out by
	/// <see cref="OnrushSprint.MilestoneCheckpointId"/>.
	/// </summary>
	public string? OnrushSprintId { get; set; }

	/// <summary>
	/// Gets or sets the onrush sprint that tracks this checkpoint.
	/// </summary>
	public OnrushSprint? OnrushSprint { get; set; }
}
