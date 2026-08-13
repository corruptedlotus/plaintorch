using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Pleiades.Diagnostics;

namespace Pleiades.Vault.Database;

/// <summary>
/// A durable, append-only record of one operation-status transition (PEP108). One row is written per transition
/// (Raised / Escalated / De-escalated / Resolved) — not per observation — so the raised→resolved timeline of an
/// operation survives restarts while staying bounded by distinct incidents rather than observation frequency.
/// </summary>
[Index(nameof(OccurredUtc))]
[Index(nameof(OperationId), nameof(ScopeKey))]
public sealed class OperationStatusEvent
{
	/// <summary>Gets or sets the database identity of the event.</summary>
	[Key]
	public long Id { get; set; }

	/// <summary>Gets or sets when the transition occurred.</summary>
	public DateTimeOffset OccurredUtc { get; set; }

	/// <summary>Gets or sets the transition kind.</summary>
	public OperationStatusTransitionKind Transition { get; set; }

	/// <summary>Gets or sets the operation the status belongs to.</summary>
	public required string OperationId { get; set; }

	/// <summary>Gets or sets the scope the status applies to (typically a vault path or entity id).</summary>
	public required string ScopeKey { get; set; }

	/// <summary>Gets or sets the failing condition's reason code.</summary>
	public required string ReasonCode { get; set; }

	/// <summary>Gets or sets the severity after the transition (for a resolution, the severity it held while active).</summary>
	public OperationSeverity Severity { get; set; }

	/// <summary>Gets or sets the severity before the transition, when it changed.</summary>
	public OperationSeverity? PreviousSeverity { get; set; }

	/// <summary>Gets or sets the JSON-encoded list of files involved, when any.</summary>
	public string? FilesJson { get; set; }

	/// <summary>Gets or sets the entity identity involved, when known.</summary>
	public string? EntityId { get; set; }

	/// <summary>Gets or sets the human-readable detail captured at the transition.</summary>
	public string? Detail { get; set; }

	/// <summary>Gets or sets the occurrence count captured at the transition.</summary>
	public int OccurrenceCount { get; set; }
}
