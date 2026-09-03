using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Pleiades.Diagnostics;

namespace Pleiades.Vault.Database;

/// <summary>
/// A durable, user-made status dismissal (PEP108 dismiss feature). Rows survive restarts and are loaded back into the
/// live <see cref="OperationStatusRegistry"/> when a vault session activates, so a snoozed issue stays snoozed across
/// runs. The primary key is the dismissal's natural key (<see cref="OperationStatusDismissalKey"/>), making a repeat
/// dismissal of the same target idempotent and a restore a single delete.
/// </summary>
[Index(nameof(Scope))]
public sealed class OperationStatusDismissalRecord
{
	/// <summary>Gets or sets the dismissal's stable natural key (scope + the identity parts the scope matches on).</summary>
	[Key]
	public required string Key { get; set; }

	/// <summary>Gets or sets the breadth the dismissal applies to.</summary>
	public OperationStatusDismissalScope Scope { get; set; }

	/// <summary>Gets or sets the operation id the dismissal targets (Instance scope).</summary>
	public required string OperationId { get; set; }

	/// <summary>Gets or sets the status scope key — typically a file path (Instance and File scope).</summary>
	public required string ScopeKey { get; set; }

	/// <summary>Gets or sets the failing condition's reason code (Instance and Reason scope).</summary>
	public required string ReasonCode { get; set; }

	/// <summary>Gets or sets the status detail captured at dismiss time; an Instance dismissal matches only while the
	/// live status's detail still equals it. Null for File/Reason scope.</summary>
	public string? Fingerprint { get; set; }

	/// <summary>Gets or sets when the dismissal was made.</summary>
	public DateTimeOffset DismissedUtc { get; set; }
}
