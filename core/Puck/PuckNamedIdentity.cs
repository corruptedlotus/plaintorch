namespace Pleiades.Puck;

/// <summary>
/// Centralizes the <c>{PUCK token} - {Title}</c> naming convention used by PUCK-backed files.
/// </summary>
public static class PuckNamedIdentity
{
	/// <summary>
	/// Gets the canonical separator between a PUCK token and title.
	/// </summary>
	public const string Separator = " - ";

	/// <summary>
	/// Formats a PUCK token and title into the canonical display form.
	/// </summary>
	/// <param name="id">The PUCK token.</param>
	/// <param name="title">The human-readable title.</param>
	/// <returns>The formatted identity.</returns>
	public static string Format(string id, string title)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		return $"{id}{Separator}{title}";
	}

	/// <summary>
	/// Formats a PUCK token and title into a filesystem-safe filename segment.
	/// </summary>
	/// <param name="id">The PUCK token.</param>
	/// <param name="title">The human-readable title.</param>
	/// <returns>The sanitized filename segment.</returns>
	public static string FormatFileName(string id, string title)
	{
		return SanitizeFileName(Format(id, title));
	}

	/// <summary>
	/// Formats a title-only filename segment for user-authored files that do not yet have a PUCK token.
	/// </summary>
	/// <param name="title">The human-readable title.</param>
	/// <returns>The sanitized filename segment.</returns>
	public static string FormatTitleOnlyFileName(string title)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		return SanitizeFileName(title);
	}

	/// <summary>
	/// Parses the canonical <c>{PUCK token} - {Title}</c> representation.
	/// </summary>
	/// <param name="value">The value to parse.</param>
	/// <returns>The parsed PUCK token and title.</returns>
	public static (string Id, string Title) Parse(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		var separatorIndex = value.IndexOf(Separator, StringComparison.Ordinal);
		if (separatorIndex <= 0 || separatorIndex + Separator.Length >= value.Length)
		{
			throw new FormatException($"Value '{value}' does not follow the expected '{{PUCK token}} - {{Title}}' convention.");
		}

		return (value[..separatorIndex], value[(separatorIndex + Separator.Length)..]);
	}

	/// <summary>
	/// Tries to parse the canonical <c>{PUCK token} - {Title}</c> representation.
	/// </summary>
	/// <param name="value">The value to parse.</param>
	/// <param name="id">Receives the parsed PUCK token when parsing succeeds.</param>
	/// <param name="title">Receives the parsed title when parsing succeeds.</param>
	/// <returns><see langword="true"/> when parsing succeeded; otherwise, <see langword="false"/>.</returns>
	public static bool TryParse(string? value, out string? id, out string? title)
	{
		id = null;
		title = null;

		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var separatorIndex = value.IndexOf(Separator, StringComparison.Ordinal);
		if (separatorIndex <= 0 || separatorIndex + Separator.Length >= value.Length)
		{
			return false;
		}

		id = value[..separatorIndex];
		title = value[(separatorIndex + Separator.Length)..];
		return true;
	}

	/// <summary>
	/// Parses either the canonical <c>{PUCK token} - {Title}</c> representation or a title-only value.
	/// </summary>
	/// <param name="value">The value to parse.</param>
	/// <returns>The parsed optional PUCK token and title.</returns>
	public static (string? Id, string Title) ParseLoose(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		return TryParse(value, out var id, out var title)
			? (id, title!)
			: ((string?)null, value.Trim());
	}

	/// <summary>
	/// Parses a file path whose file name follows the canonical <c>{PUCK token} - {Title}</c> representation.
	/// </summary>
	/// <param name="path">The file path to parse.</param>
	/// <returns>The parsed PUCK token and title.</returns>
	public static (string Id, string Title) ParsePath(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return Parse(Path.GetFileNameWithoutExtension(path));
	}

	/// <summary>
	/// Parses a file path whose file name may either follow the canonical <c>{PUCK token} - {Title}</c>
	/// representation or a title-only form.
	/// </summary>
	/// <param name="path">The file path to parse.</param>
	/// <returns>The parsed optional PUCK token and title.</returns>
	public static (string? Id, string Title) ParseLoosePath(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return ParseLoose(Path.GetFileNameWithoutExtension(path));
	}

	/// <summary>
	/// Applies a parsed PUCK token and title to a target named entity.
	/// </summary>
	/// <typeparam name="T">The entity type.</typeparam>
	/// <param name="entity">The target entity.</param>
	/// <param name="value">The formatted identity value.</param>
	public static void ApplyTo<T>(T entity, string value)
		where T : IPuckNamedEntity
	{
		ArgumentNullException.ThrowIfNull(entity);
		var (id, title) = Parse(value);
		entity.Id = id;
		entity.Title = title;
	}

	private static string SanitizeFileName(string value)
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		var builder = new System.Text.StringBuilder(value.Length);
		foreach (var character in value)
		{
			builder.Append(invalidChars.Contains(character) ? '_' : character);
		}

		return builder.ToString();
	}
}