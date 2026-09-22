using System.Text.Json;

namespace Pleiades.Plaintorch.Preferences;

/// <summary>The value shape of a preference, so a client can render and validate it without hard-coding each key.</summary>
public enum PreferenceKind
{
	/// <summary>A whole number.</summary>
	Integer,

	/// <summary>A true/false toggle.</summary>
	Boolean,

	/// <summary>One of a fixed set of named options (see <see cref="PreferenceDescriptor.Options"/>).</summary>
	Enum,

	/// <summary>Free text.</summary>
	String,
}

/// <summary>
/// Describes one user preference for enumeration and validation (PEP116): its key, how to render it, its default,
/// and — for an <see cref="PreferenceKind.Enum"/> — its allowed option names. The catalog of these is what the
/// settings surface renders, so a new preference appears automatically once it is added to
/// <see cref="PreferenceCatalog"/>.
/// </summary>
public sealed record PreferenceDescriptor
{
	/// <summary>The storage key (a <see cref="PreferenceKeys"/> constant).</summary>
	public required string Key { get; init; }

	/// <summary>A short human label for the control.</summary>
	public required string Label { get; init; }

	/// <summary>The section the preference belongs to (e.g. "Watcher", "Agenda", "CalDAV").</summary>
	public required string Group { get; init; }

	/// <summary>An optional one-line explanation shown under the control.</summary>
	public string? Description { get; init; }

	/// <summary>The value shape.</summary>
	public required PreferenceKind Kind { get; init; }

	/// <summary>The code-owned default, as its stored JSON form.</summary>
	public required string DefaultRaw { get; init; }

	/// <summary>For <see cref="PreferenceKind.Enum"/>, the allowed option names; otherwise <see langword="null"/>.</summary>
	public IReadOnlyList<string>? Options { get; init; }

	/// <summary>Whether a supplied JSON value is acceptable for this preference's kind.</summary>
	public bool Accepts(JsonElement value) => Kind switch
	{
		PreferenceKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
		PreferenceKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
		PreferenceKind.Enum => value.ValueKind == JsonValueKind.String && Options is not null && Options.Contains(value.GetString()),
		PreferenceKind.String => value.ValueKind == JsonValueKind.String,
		_ => false,
	};
}

/// <summary>Thrown when a preference write carries a value that does not match the preference's declared kind.</summary>
public sealed class PreferenceValidationException(string message) : Exception(message);
