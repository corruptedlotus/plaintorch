using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pleiades.Plaintorch.Api.Contracts;

/// <summary>
/// A tri-state update field. It distinguishes three cases a plain nullable cannot: <b>absent</b>
/// (<see cref="IsSet"/> is <see langword="false"/> — leave the target unchanged), and <b>present</b> with a
/// value that may itself be <see langword="null"/> (a deliberate clear) or non-null (a set).
///
/// This is what lets an update endpoint treat an explicit <c>null</c> as canonical — "clear this field" — while
/// an omitted key still means "no change", removing the need for per-field <c>Clear*</c> flags. Use it as the
/// type of a positional update-record parameter with a <c>= default</c> default:
/// <code>public sealed record FooUpdate(Optional&lt;int?&gt; Bar = default);</code>
/// It deserializes through <see cref="OptionalJsonConverterFactory"/>, which runs only when the JSON key is
/// present — so an omitted key leaves the <c>default</c> (unset).
/// </summary>
public readonly struct Optional<T>
{
	public Optional(T value)
	{
		Value = value;
		IsSet = true;
	}

	/// <summary>Whether the field was present in the payload and should therefore be applied.</summary>
	public bool IsSet { get; }

	/// <summary>The supplied value; meaningful only when <see cref="IsSet"/> is true. May be <see langword="null"/> (a clear).</summary>
	public T Value { get; }

	public static implicit operator Optional<T>(T value) => new(value);
}

/// <summary>
/// Deserializes <see cref="Optional{T}"/> so a present JSON key — value or explicit <c>null</c> — becomes a set
/// value, while an omitted key leaves the record parameter's default (unset). System.Text.Json invokes a
/// property's converter only when the key is present, which is exactly the presence signal the tri-state needs.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
	{
		return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);
	}

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		var valueType = typeToConvert.GetGenericArguments()[0];
		var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(valueType);
		return (JsonConverter)Activator.CreateInstance(converterType)!;
	}

	private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
	{
		// The key is present but its value may be null (a clear), so the converter must be given the null token.
		public override bool HandleNull => true;

		public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			return new Optional<T>(JsonSerializer.Deserialize<T>(ref reader, options)!);
		}

		public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
		{
			if (value.IsSet)
			{
				JsonSerializer.Serialize(writer, value.Value, options);
			}
			else
			{
				writer.WriteNullValue();
			}
		}
	}
}
