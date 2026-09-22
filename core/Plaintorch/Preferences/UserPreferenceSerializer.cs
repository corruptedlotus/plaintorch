using System.Text.Json;

namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Serializes preference values to and from the compact JSON strings held in the key/value table (PEP116).
/// </summary>
/// <remarks>
/// Deserialization is <em>total</em>: a malformed, empty, or type-mismatched stored value yields the caller's
/// default rather than throwing. This is what lets a preference's type change, or a hand-edited row, degrade to
/// the code-owned default on read instead of breaking a consumer.
/// </remarks>
public static class UserPreferenceSerializer
{
	/// <summary>Serializes a preference value to its stored JSON form.</summary>
	public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

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
			var value = JsonSerializer.Deserialize<T>(raw);
			return value is null ? fallback : value;
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or FormatException or OverflowException)
		{
			return fallback;
		}
	}
}
