using Pleiades.Puck;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a checkpoint: a simple point in the journey that only aggregates dependencies (PEP101). A
/// checkpoint has no lifecycle of its own; once all of its incoming dependencies are met it <em>unlocks</em>
/// (unblocking its dependants), unless it still owes a Celestron toll or an external condition.
/// </summary>
/// <remarks>
/// Checkpoints are PUCK-addressable but database-only (no <c>VaultStorage</c>): they carry no markdown file.
/// Because they have no begin/finish, a checkpoint endpoint always leaves the dependency's trigger (as source)
/// or constraint (as target) empty.
/// </remarks>
[PuckEntity("checkpoint")]
[PuckFormat("cc{S:6}")]
public sealed class Checkpoint : PuckNamedEntity
{
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
}
