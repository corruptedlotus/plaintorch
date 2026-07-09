using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Stores audit trail entries for application, discovery, and synchronization activity using soft references only.
/// </summary>
[Index(nameof(OccurredUtc))]
[Index(nameof(SubjectType), nameof(SubjectId))]
[Index(nameof(Action))]
public sealed class AuditLogEntry
{
	/// <summary>
	/// Gets or sets the database identity of the audit entry.
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the event timestamp.
	/// </summary>
	public DateTimeOffset OccurredUtc { get; set; }

	/// <summary>
	/// Gets or sets the broad event category.
	/// </summary>
	public required string Category { get; set; }

	/// <summary>
	/// Gets or sets the event action name.
	/// </summary>
	public required string Action { get; set; }

	/// <summary>
	/// Gets or sets the subject type, when one exists.
	/// </summary>
	public string? SubjectType { get; set; }

	/// <summary>
	/// Gets or sets the subject identifier, when one exists.
	/// </summary>
	public string? SubjectId { get; set; }

	/// <summary>
	/// Gets or sets the subject title, when one exists.
	/// </summary>
	public string? SubjectTitle { get; set; }

	/// <summary>
	/// Gets or sets the referenced temporal entry kind, when one exists.
	/// </summary>
	public string? TemporalKind { get; set; }

	/// <summary>
	/// Gets or sets the referenced temporal entry key, when one exists.
	/// </summary>
	public string? TemporalEntryKey { get; set; }

	/// <summary>
	/// Gets or sets the referenced temporal entity type, when one exists.
	/// </summary>
	public string? TemporalEntityType { get; set; }

	/// <summary>
	/// Gets or sets the referenced temporal entity identifier, when one exists.
	/// </summary>
	public string? TemporalEntityId { get; set; }

	/// <summary>
	/// Gets or sets the referenced temporal entity title, when one exists.
	/// </summary>
	public string? TemporalEntityTitle { get; set; }

	/// <summary>
	/// Gets or sets the referenced temporal file location, when one exists.
	/// </summary>
	public string? TemporalLocation { get; set; }

	/// <summary>
	/// Gets or sets optional structured event details.
	/// </summary>
	public string? DetailsJson { get; set; }
}