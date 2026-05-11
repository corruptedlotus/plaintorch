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
		await context.SaveChangesAsync(cancellationToken);
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