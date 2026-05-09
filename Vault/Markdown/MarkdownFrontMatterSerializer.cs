using System.Globalization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using Pleiades.Puck;

namespace Pleiades.Vault.Markdown;

/// <summary>
/// Serializes annotated models into Obsidian-friendly YAML frontmatter and parses it back into a raw key/value view.
/// </summary>
public sealed class MarkdownFrontMatterSerializer(PuckTokenizer puckTokenizer)
{
	private static readonly NullabilityInfoContext NullabilityContext = new();

	/// <summary>
	/// Serializes a model into markdown with frontmatter and an optional body.
	/// </summary>
	/// <typeparam name="T">The model type.</typeparam>
	/// <param name="model">The model instance to serialize.</param>
	/// <param name="body">Optional markdown body content.</param>
	/// <returns>The generated markdown document.</returns>
	public string Serialize<T>(T model, string body = "")
	{
		ArgumentNullException.ThrowIfNull(model);

		var lines = new List<string> { "---" };
		foreach (var property in GetAnnotatedProperties(typeof(T)))
		{
			var attribute = property.GetCustomAttribute<MarkdownFieldAttribute>()!;
			var value = property.GetValue(model);
			if (value is null)
			{
				continue;
			}

			lines.Add($"{attribute.Name}: {FormatScalar(value)}");
		}

		lines.Add("---");
		if (!string.IsNullOrWhiteSpace(body))
		{
			lines.Add(string.Empty);
			lines.Add(body.TrimEnd());
		}

		return string.Join(Environment.NewLine, lines) + Environment.NewLine;
	}

	/// <summary>
	/// Parses the frontmatter section of a markdown document into a case-insensitive dictionary.
	/// </summary>
	/// <param name="markdown">The markdown content to inspect.</param>
	/// <returns>A dictionary containing parsed frontmatter values.</returns>
	public IReadOnlyDictionary<string, string> ParseFrontMatter(string markdown)
	{
		ArgumentNullException.ThrowIfNull(markdown);

		using var reader = new StringReader(markdown);
		if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}

		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		while (reader.ReadLine() is { } line)
		{
			if (line == "---")
			{
				break;
			}

			var separatorIndex = line.IndexOf(':');
			if (separatorIndex <= 0)
			{
				continue;
			}

			var key = line[..separatorIndex].Trim();
			var value = line[(separatorIndex + 1)..].Trim();
			values[key] = value;
		}

		return values;
	}

	/// <summary>
	/// Maps markdown frontmatter into a new CLR model instance and validates the converted values.
	/// </summary>
	/// <typeparam name="T">The model type.</typeparam>
	/// <param name="markdown">The markdown content to deserialize.</param>
	/// <returns>The hydrated model and any validation issues.</returns>
	public MarkdownDeserializationResult<T> Deserialize<T>(string markdown)
		where T : class, new()
	{
		return Deserialize(markdown, new T());
	}

	/// <summary>
	/// Maps markdown frontmatter into an existing CLR model instance and validates the converted values.
	/// </summary>
	/// <typeparam name="T">The model type.</typeparam>
	/// <param name="markdown">The markdown content to deserialize.</param>
	/// <param name="model">The target model to populate.</param>
	/// <returns>The hydrated model and any validation issues.</returns>
	public MarkdownDeserializationResult<T> Deserialize<T>(string markdown, T model)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(markdown);
		ArgumentNullException.ThrowIfNull(model);

		var frontMatter = ParseFrontMatter(markdown);
		var issues = new List<MarkdownValidationIssue>();
		PopulateAnnotatedProperties(model, typeof(T), frontMatter, issues, null);
		ValidateNestedAnnotatedProperties(model, typeof(T), issues, null);
		return new MarkdownDeserializationResult<T>(model, issues);
	}

	/// <summary>
	/// Validates markdown frontmatter against a CLR model shape without requiring callers to inspect the hydrated model.
	/// </summary>
	/// <typeparam name="T">The model type.</typeparam>
	/// <param name="markdown">The markdown content to validate.</param>
	/// <returns>The validation issues found while parsing the markdown.</returns>
	public IReadOnlyList<MarkdownValidationIssue> Validate<T>(string markdown)
		where T : class, new()
	{
		return Deserialize<T>(markdown).Issues;
	}

	/// <summary>
	/// Validates markdown frontmatter against an existing CLR model shape.
	/// </summary>
	/// <typeparam name="T">The model type.</typeparam>
	/// <param name="markdown">The markdown content to validate.</param>
	/// <param name="model">The target model to populate during validation.</param>
	/// <returns>The validation issues found while parsing the markdown.</returns>
	public IReadOnlyList<MarkdownValidationIssue> Validate<T>(string markdown, T model)
		where T : class
	{
		return Deserialize(markdown, model).Issues;
	}

	private static IEnumerable<PropertyInfo> GetAnnotatedProperties(Type type)
	{
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetCustomAttribute<MarkdownFieldAttribute>() is not null);
	}

	private static string FormatScalar(object value)
	{
		return value switch
		{
			string text => Escape(text),
			DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
			DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
			Enum enumValue => enumValue.ToString(),
			IEnumerable<string> strings => JsonSerializer.Serialize(strings),
			_ when value.GetType().IsClass && value.GetType() != typeof(string) => JsonSerializer.Serialize(value),
			_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
		};
	}

	private static string Escape(string value)
	{
		if (value.Contains(':') || value.Contains('#') || value.Contains('[') || value.Contains(']'))
		{
			return JsonSerializer.Serialize(value);
		}

		return value;
	}

	private void PopulateAnnotatedProperties(
		object model,
		Type modelType,
		IReadOnlyDictionary<string, string> frontMatter,
		ICollection<MarkdownValidationIssue> issues,
		string? pathPrefix)
	{
		foreach (var property in GetAnnotatedProperties(modelType))
		{
			var attribute = property.GetCustomAttribute<MarkdownFieldAttribute>()!;
			var fieldPath = ComposeFieldPath(pathPrefix, attribute.Name);

			if (!frontMatter.TryGetValue(attribute.Name, out var rawValue))
			{
				if (!CanBeNull(property))
				{
					issues.Add(new MarkdownValidationIssue(fieldPath, "Field is required."));
				}

				continue;
			}

			if (!TryConvertPropertyValue(property, rawValue, fieldPath, out var convertedValue, out var issue))
			{
				issues.Add(issue!);
				continue;
			}

			property.SetValue(model, convertedValue);
		}
	}

	private void ValidateNestedAnnotatedProperties(
		object model,
		Type modelType,
		ICollection<MarkdownValidationIssue> issues,
		string? pathPrefix)
	{
		foreach (var property in GetAnnotatedProperties(modelType))
		{
			var attribute = property.GetCustomAttribute<MarkdownFieldAttribute>()!;
			var fieldPath = ComposeFieldPath(pathPrefix, attribute.Name);
			var propertyValue = property.GetValue(model);

			if (propertyValue is null)
			{
				if (!CanBeNull(property) && !string.IsNullOrWhiteSpace(pathPrefix))
				{
					issues.Add(new MarkdownValidationIssue(fieldPath, "Field is required."));
				}

				continue;
			}

			var propertyType = UnwrapNullableType(property.PropertyType);
			if (propertyType.IsEnum && !Enum.IsDefined(propertyType, propertyValue))
			{
				issues.Add(new MarkdownValidationIssue(fieldPath, $"Value '{propertyValue}' is not a defined {propertyType.Name} state.", Convert.ToString(propertyValue, CultureInfo.InvariantCulture)));
				continue;
			}

			if (propertyValue is string relationalId && TryGetRelatedEntityType(property, out var relatedEntityType))
			{
				ValidateRelatedPuck(fieldPath, relationalId, relatedEntityType, issues);
				continue;
			}

			if (HasAnnotatedProperties(propertyType))
			{
				ValidateNestedAnnotatedProperties(propertyValue, propertyType, issues, fieldPath);
			}
		}
	}

	private bool TryConvertPropertyValue(
		PropertyInfo property,
		string rawValue,
		string fieldPath,
		out object? convertedValue,
		out MarkdownValidationIssue? issue)
	{
		var propertyType = UnwrapNullableType(property.PropertyType);
		var canBeNull = CanBeNull(property);

		if (string.IsNullOrWhiteSpace(rawValue))
		{
			if (canBeNull)
			{
				convertedValue = null;
				issue = null;
				return true;
			}

			convertedValue = null;
			issue = new MarkdownValidationIssue(fieldPath, "Field cannot be empty.", rawValue);
			return false;
		}

		var normalizedValue = UnwrapString(rawValue);

		try
		{
			if (propertyType == typeof(string))
			{
				convertedValue = normalizedValue;
				issue = null;
				return true;
			}

			if (propertyType.IsEnum)
			{
				if (!Enum.TryParse(propertyType, normalizedValue, true, out var enumValue) || !Enum.IsDefined(propertyType, enumValue!))
				{
					convertedValue = null;
					issue = new MarkdownValidationIssue(
						fieldPath,
						$"Value '{normalizedValue}' is not a valid {propertyType.Name} state. Expected one of: {string.Join(", ", Enum.GetNames(propertyType))}.",
						rawValue);
					return false;
				}

				convertedValue = enumValue;
				issue = null;
				return true;
			}

			if (TryConvertSimpleValue(propertyType, normalizedValue, out convertedValue))
			{
				issue = null;
				return true;
			}

			if (TryConvertStringCollection(property, rawValue, out convertedValue))
			{
				issue = null;
				return true;
			}

			convertedValue = JsonSerializer.Deserialize(rawValue, propertyType, JsonSerializerOptions.Default);
			issue = null;
			return true;
		}
		catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException or OverflowException)
		{
			convertedValue = null;
			issue = new MarkdownValidationIssue(fieldPath, exception.Message, rawValue);
			return false;
		}
	}

	private void ValidateRelatedPuck(string fieldPath, string relationalId, Type relatedEntityType, ICollection<MarkdownValidationIssue> issues)
	{
		try
		{
			puckTokenizer.TokenizeFor(relatedEntityType, relationalId);
		}
		catch (Exception exception) when (exception is FormatException or InvalidOperationException or NotSupportedException)
		{
			issues.Add(new MarkdownValidationIssue(fieldPath, $"Value '{relationalId}' is not a valid {relatedEntityType.Name} PUCK: {exception.Message}", relationalId));
		}
	}

	private static bool TryConvertSimpleValue(Type propertyType, string normalizedValue, out object? convertedValue)
	{
		if (propertyType == typeof(bool) && bool.TryParse(normalizedValue, out var boolValue))
		{
			convertedValue = boolValue;
			return true;
		}

		if (propertyType == typeof(int) && int.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
		{
			convertedValue = intValue;
			return true;
		}

		if (propertyType == typeof(long) && long.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
		{
			convertedValue = longValue;
			return true;
		}

		if (propertyType == typeof(DateOnly) && DateOnly.TryParseExact(normalizedValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnlyValue))
		{
			convertedValue = dateOnlyValue;
			return true;
		}

		if (propertyType == typeof(DateTimeOffset) && DateTimeOffset.TryParse(normalizedValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffsetValue))
		{
			convertedValue = dateTimeOffsetValue;
			return true;
		}

		if (propertyType == typeof(DateTime) && DateTime.TryParse(normalizedValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeValue))
		{
			convertedValue = dateTimeValue;
			return true;
		}

		convertedValue = null;
		return false;
	}

	private static bool TryConvertStringCollection(PropertyInfo property, string rawValue, out object? convertedValue)
	{
		var propertyType = UnwrapNullableType(property.PropertyType);
		if (!typeof(IEnumerable<string>).IsAssignableFrom(propertyType))
		{
			convertedValue = null;
			return false;
		}

		List<string>? values;
		if (rawValue.TrimStart().StartsWith("[", StringComparison.Ordinal))
		{
			values = JsonSerializer.Deserialize<List<string>>(rawValue, JsonSerializerOptions.Default);
		}
		else
		{
			values = rawValue
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(UnwrapString)
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.ToList();
		}

		values ??= [];

		if (propertyType.IsAssignableFrom(typeof(List<string>)))
		{
			convertedValue = values;
			return true;
		}

		if (propertyType.IsAssignableFrom(typeof(string[])))
		{
			convertedValue = values.ToArray();
			return true;
		}

		convertedValue = values;
		return true;
	}

	private static string ComposeFieldPath(string? prefix, string fieldName)
	{
		return string.IsNullOrWhiteSpace(prefix) ? fieldName : $"{prefix}.{fieldName}";
	}

	private static bool TryGetRelatedEntityType(PropertyInfo property, out Type relatedEntityType)
	{
		relatedEntityType = null!;
		var declaringType = property.DeclaringType;
		if (declaringType is null)
		{
			return false;
		}

		foreach (var candidate in declaringType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			var foreignKeyAttribute = candidate.GetCustomAttribute<ForeignKeyAttribute>();
			if (!string.Equals(foreignKeyAttribute?.Name, property.Name, StringComparison.Ordinal))
			{
				continue;
			}

			relatedEntityType = UnwrapNullableType(candidate.PropertyType);
			return true;
		}

		return false;
	}

	private static bool HasAnnotatedProperties(Type type)
	{
		return GetAnnotatedProperties(type).Any();
	}

	private static Type UnwrapNullableType(Type type)
	{
		return Nullable.GetUnderlyingType(type) ?? type;
	}

	private static bool CanBeNull(PropertyInfo property)
	{
		if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
		{
			return true;
		}

		if (property.PropertyType.IsValueType)
		{
			return false;
		}

		return NullabilityContext.Create(property).WriteState != NullabilityState.NotNull;
	}

	private static string UnwrapString(string rawValue)
	{
		var trimmed = rawValue.Trim();
		if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
		{
			return JsonSerializer.Deserialize<string>(trimmed, JsonSerializerOptions.Default) ?? string.Empty;
		}

		return trimmed;
	}
}