using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// A durable record that an entity's markdown file is out of step with its database state and awaits the vault drainer
/// (PEP110 Refactor BETA). The core records an intent in the same transaction as the entity change; the drainer writes
/// the file and clears the row, so a crash between the database commit and the file write is recovered by the startup
/// drain. It is a coalesced dirty-set — one row per entity, keyed by its type and id, so a repeat change idempotently
/// upserts the row and a successful drain deletes it — so the table is normally empty (mirroring the outbox discipline
/// of <see cref="OperationStatusDismissalRecord"/>). The intent carries no diff: the drainer reconciles from the
/// current database state and the existing file.
/// </summary>
[PrimaryKey(nameof(EntityType), nameof(EntityId))]
public sealed class VaultWriteIntent
{
	/// <summary>Gets or sets the entity's concrete CLR type name (<see cref="System.Type.FullName"/>), used to reload it.</summary>
	public required string EntityType { get; set; }

	/// <summary>Gets or sets the entity's PUCK identifier.</summary>
	public required string EntityId { get; set; }

	/// <summary>Gets or sets what the drainer must do: reconcile the file to state, or remove it.</summary>
	public VaultWriteIntentKind Kind { get; set; }

	/// <summary>
	/// Gets or sets the entity's PUCK identity captured at enqueue — used to locate the file for a
	/// <see cref="VaultWriteIntentKind.Remove"/> whose database row is already gone.
	/// </summary>
	public string? Identity { get; set; }

	/// <summary>
	/// Gets or sets the entity's last-known vault-relative note path — a locate fallback for a
	/// <see cref="VaultWriteIntentKind.Remove"/>.
	/// </summary>
	public string? LastKnownPath { get; set; }

	/// <summary>Gets or sets when the intent was recorded (FIFO tiebreak and diagnostics).</summary>
	public DateTimeOffset EnqueuedUtc { get; set; }
}
