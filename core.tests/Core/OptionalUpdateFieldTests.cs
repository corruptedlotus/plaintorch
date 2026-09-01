using System.Text.Json;
using Pleiades.Plaintorch.Api.Contracts;
using Xunit;

namespace Pleiades.Tests;

/// <summary>
/// Proves the tri-state <see cref="Optional{T}"/> update field: an omitted key is unset (leave unchanged), an
/// explicit value is a set, and an explicit null is a set-to-null (a clear) — the distinction a plain nullable
/// cannot express, and the basis for treating null as a canonical clear in update endpoints.
/// </summary>
public sealed class OptionalUpdateFieldTests
{
	private static readonly JsonSerializerOptions Options = CreateOptions();

	private static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new OptionalJsonConverterFactory());
		return options;
	}

	[Fact]
	public void Omitted_key_is_unset()
	{
		var update = JsonSerializer.Deserialize<ExecutiveUpdate>("{}", Options)!;
		Assert.False(update.Estimation.IsSet);
	}

	[Fact]
	public void Explicit_value_is_a_set()
	{
		var update = JsonSerializer.Deserialize<ExecutiveUpdate>("""{"estimation":45}""", Options)!;
		Assert.True(update.Estimation.IsSet);
		Assert.Equal(45, update.Estimation.Value);
	}

	[Fact]
	public void Explicit_null_is_a_clear()
	{
		var update = JsonSerializer.Deserialize<ExecutiveUpdate>("""{"estimation":null}""", Options)!;
		Assert.True(update.Estimation.IsSet);
		Assert.Null(update.Estimation.Value);
	}
}
