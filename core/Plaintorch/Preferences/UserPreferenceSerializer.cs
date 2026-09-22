using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Serializes preference values to and from the compact JSON strings held in the key/value table (PEP116).
/// </summary>
/// <remarks>
/// Enums are stored <em>by name</em> (not ordinal), so reordering an enum's members never silently reinterprets a
/// stored value. Deserialization is <em>total</em>: a malformed, empty, or type-mismatched stored value yields
/// the caller's default rather than throwing, which is what lets a preference's type change, or a hand-edited
/// row, degrade to the code-owned default on read instead of breaking a consumer.
/// </remarks>
public static class UserPreferenceSerializer
{
	private static readonly JsonSerializerOptions Options = new()
	{
		Converters = { new JsonStringEnumConverter() },
	};

	/// <summary>Serializes a preference value to its stored JSON form.</summary>
	public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

	/// <summary>
	/// Deserializes a stored value, returning <paramref name="fallback"/> when it is absent or cannot be read
	/// as <typeparamref name="T"/>.
	/// </summary>
	public static T Deserialize<T>(string? raw, T fallback)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return fallback;
		}

		try
		{
			var value = JsonSerializer.Deserialize<T>(raw, Options);
			return value is null ? fallback : value;
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or FormatException or OverflowException)
		{
			return fallback;
		}
	}
}
