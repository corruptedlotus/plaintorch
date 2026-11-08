using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents the occurrence of an item's due (PEP103 patch 7): the one terminative an <see cref="IDueOwner"/> — an
/// objective, a stellar directive or a checkpoint — spawns from its deadline. It sits on the agenda beside the eventives,
/// and is projected while its origin's due acts and hardened like any occurrence, by time or by interaction.
/// </summary>
/// <remarks>
/// <para>
/// A terminative is its origin's mirror, kept in step by the state engine even once hardened: its position in time follows
/// the due (an all-day due is an all-day occurrence, a timed one a minute-granular one), a due cleared takes a hardened
/// terminative away, and deleting the origin deletes it. Its <see cref="Resolution"/> is the engine's alone, read from the
/// origin's lifecycle. The one thing a user sets is <see cref="Ignored"/>.
/// </para>
/// <para>
/// An origin has at most one terminative, so the origin alone identifies it: its <see cref="Occurrence.RecurrenceId"/> is
/// the due's moment, and moves with the due rather than pinning a first slot as a recurring occurrence's does. It carries
/// the owner columns of every kind that can own one, exactly one of them set, and is not a dependency endpoint: an edge
/// names the origin itself.
/// </para>
/// </remarks>
public sealed class Terminative : Occurrence
{
	/// <summary>
	/// Gets or sets the owning objective identifier, for an objective's due.
	/// </summary>
	public string? ObjectiveId { get; set; }

	[ForeignKey(nameof(ObjectiveId))]
	/// <summary>
	/// Gets or sets the owning objective.
	/// </summary>
	public Objective? Objective { get; set; }

	/// <summary>
	/// Gets or sets the owning stellar directive identifier, for a stellar directive's due.
	/// </summary>
	public string? DirectiveId { get; set; }

	[ForeignKey(nameof(DirectiveId))]
	/// <summary>
	/// Gets or sets the owning stellar directive.
	/// </summary>
	public StellarDirective? Directive { get; set; }

	/// <summary>
	/// Gets or sets the owning checkpoint identifier, for a checkpoint's due.
	/// </summary>
	public string? CheckpointId { get; set; }

	[ForeignKey(nameof(CheckpointId))]
	/// <summary>
	/// Gets or sets the owning checkpoint.
	/// </summary>
	public Checkpoint? Checkpoint { get; set; }

	/// <inheritdoc />
	[NotMapped]
	public override string RecurrenceOwnerUid => ObjectiveId ?? DirectiveId ?? CheckpointId ?? string.Empty;

	/// <summary>
	/// Gets the loaded origin, or <see langword="null"/> when its navigation is not loaded.
	/// </summary>
	[NotMapped, JsonIgnore]
	public IDueOwner? Origin => (IDueOwner?)Objective ?? (IDueOwner?)Directive ?? Checkpoint;

	/// <summary>
	/// Gets or sets how the terminative resolved. The state engine sets it from the origin's lifecycle; no request does.
	/// </summary>
	[LifecycleStatus]
	public TerminativeResolution Resolution { get; set; } = LifecycleDeclarations.InitialStatus<TerminativeResolution>();

	/// <summary>
	/// Gets or sets whether the user has raised the ignore flag: the terminative stays, following its origin as ever, and the
	/// agenda no longer lists it. The one field of a terminative a user sets, and the engine never does.
	/// </summary>
	public bool Ignored { get; set; }

	/// <summary>
	/// Builds the terminative of an origin's due, owned through the origin's own column and placed on its due. Its
	/// resolution is left to the state engine.
	/// </summary>
	/// <exception cref="InvalidOperationException">The origin has no due.</exception>
	public static Terminative Of(IDueOwner origin)
	{
		ArgumentNullException.ThrowIfNull(origin);
		var due = origin.Due ?? throw new InvalidOperationException($"{origin.GetType().Name} '{origin.Id}' has no due to spawn a terminative.");
		var terminative = origin switch
		{
			Objective objective => new Terminative { ObjectiveId = objective.Id },
			StellarDirective directive => new Terminative { DirectiveId = directive.Id },
			Checkpoint checkpoint => new Terminative { CheckpointId = checkpoint.Id },
			_ => throw new ArgumentException($"'{origin.GetType().Name}' owns no terminative.", nameof(origin)),
		};
		terminative.Follow(due);
		return terminative;
	}

	/// <summary>
	/// Gets whether this terminative is <paramref name="origin"/>'s: owned through the origin kind's column, by its id.
	/// </summary>
	public bool BelongsTo(IDueOwner origin)
	{
		ArgumentNullException.ThrowIfNull(origin);
		var ownerId = origin switch
		{
			Orchestration.Objective => ObjectiveId,
			StellarDirective => DirectiveId,
			Orchestration.Checkpoint => CheckpointId,
			_ => null,
		};
		return string.Equals(ownerId, origin.Id, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Places the terminative on <paramref name="due"/>: a whole-day due is an all-day occurrence, a timed one a
	/// minute-granular occurrence anchored in the due's zone. The epoch is changed in place, so a tracked row is updated
	/// rather than replaced, and only where it differs. Returns whether anything moved.
	/// </summary>
	public bool Follow(Due due)
	{
		ArgumentNullException.ThrowIfNull(due);
		var target = Epoch.FromSlot(due.Moment, due.IsAllDay ? null : due.TimeZone);
		var moved = Epoch.Moment != target.Moment
			|| Epoch.Granularity != target.Granularity
			|| Epoch.Duration is not null
			|| !string.Equals(Epoch.TimeZone, target.TimeZone, StringComparison.Ordinal)
			|| RecurrenceId != due.Moment;
		if (!moved)
		{
			return false;
		}

		Epoch.Moment = target.Moment;
		Epoch.Granularity = target.Granularity;
		Epoch.Duration = null;
		Epoch.TimeZone = target.TimeZone;
		RecurrenceId = due.Moment;
		return true;
	}
}
