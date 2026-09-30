using System.Text.Json;
using System.Text.Json.Serialization;
using Pleiades.Puck;

namespace Pleiades.Vault.Database;

/// <summary>
/// Writes soft-reference audit trail entries for application and synchronization activity.
/// </summary>
public sealed class VaultAuditLogService(PlainfraContext context)
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
	};

	/// <summary>
	/// Writes an audit entry.
	/// </summary>
	public async Task<AuditLogEntry> WriteAsync(
		string category,
		string action,
		object? subject = null,
		string? subjectType = null,
		string? subjectId = null,
		string? subjectTitle = null,
		string? temporalKind = null,
		string? temporalEntryKey = null,
		string? temporalEntityType = null,
		string? temporalEntityId = null,
		string? temporalEntityTitle = null,
		string? temporalLocation = null,
		object? details = null,
		CancellationToken cancellationToken = default)
	{
		var entry = Stage(category, action, subject, subjectType, subjectId, subjectTitle, temporalKind, temporalEntryKey, temporalEntityType, temporalEntityId, temporalEntityTitle, temporalLocation, details);
		await context.SaveChangesAsync(cancellationToken);
		return entry;
	}

	/// <summary>
	/// Stages an audit entry onto the current unit of work without saving, for a rule that runs inside a save (the
	/// state-policy hook) and cannot start another: the entry commits with whatever that save commits.
	/// </summary>
	public AuditLogEntry Stage(
		string category,
		string action,
		object? subject = null,
		string? subjectType = null,
		string? subjectId = null,
		string? subjectTitle = null,
		string? temporalKind = null,
		string? temporalEntryKey = null,
		string? temporalEntityType = null,
		string? temporalEntityId = null,
		string? temporalEntityTitle = null,
		string? temporalLocation = null,
		object? details = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentException.ThrowIfNullOrWhiteSpace(action);

		if (subject is not null)
		{
			var descriptor = Describe(subject);
			subjectType ??= descriptor.Type;
			subjectId ??= descriptor.Id;
			subjectTitle ??= descriptor.Title;
		}

		var entry = new AuditLogEntry
		{
			OccurredUtc = DateTimeOffset.UtcNow,
			Category = category,
			Action = action,
			SubjectType = subjectType,
			SubjectId = subjectId,
			SubjectTitle = subjectTitle,
			TemporalKind = temporalKind,
			TemporalEntryKey = temporalEntryKey,
			TemporalEntityType = temporalEntityType,
			TemporalEntityId = temporalEntityId,
			TemporalEntityTitle = temporalEntityTitle,
			TemporalLocation = temporalLocation,
			DetailsJson = details is null ? null : JsonSerializer.Serialize(details, SerializerOptions),
		};

		context.AuditLogEntries.Add(entry);
		return entry;
	}

	private static (string Type, string? Id, string? Title) Describe(object subject)
	{
		if (subject is IPuckNamedEntity namedEntity)
		{
			return (subject.GetType().Name, namedEntity.Id, namedEntity.Title);
		}

		var type = subject.GetType();
		var id = type.GetProperty("Id")?.GetValue(subject)?.ToString();
		var title = type.GetProperty("Title")?.GetValue(subject)?.ToString();
		return (type.Name, id, title);
	}
}