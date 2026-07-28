using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pleiades.Orbits;

// C# port of the @pleiades/orbits snapshot contracts. The JSON shape matches the
// TypeScript OrbitSnapshot exactly so blobs are interchangeable between engines.

/// <summary>
/// Per-node running-counter state for <c>@x</c>/<c>*x</c> limits.
/// </summary>
public sealed class OrbitCounterState
{
	[JsonPropertyName("fired")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Fired { get; set; }

	[JsonPropertyName("cycleKey")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public long? CycleKey { get; set; }

	[JsonPropertyName("iters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Iters { get; set; }

	[JsonPropertyName("lastPeriod")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public long? LastPeriod { get; set; }

	public OrbitCounterState Clone() => new() { Fired = Fired, CycleKey = CycleKey, Iters = Iters, LastPeriod = LastPeriod };
}

/// <summary>
/// A self-contained, JSON-serializable snapshot that lets a fresh engine resume a
/// stream exactly where a previous one left off. <c>(notation, epoch, seed)</c> fixes the
/// candidate stream; cursor + counters capture how far it has progressed and how much
/// of each <c>@x</c>/<c>*x</c> budget has been consumed.
/// </summary>
public sealed class OrbitSnapshot
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = 1;

	/// <summary>Re-parsed on resume; makes the blob self-contained.</summary>
	[JsonPropertyName("notation")]
	public required string Notation { get; set; }

	/// <summary>Anchor ISO instant. Full: original epoch. Promoted: last-emitted entry.</summary>
	[JsonPropertyName("epoch")]
	public required string Epoch { get; set; }

	/// <summary>Engine RNG seed; required for reproducible <c>{#n}</c> randoms.</summary>
	[JsonPropertyName("seed")]
	public long Seed { get; set; }

	/// <summary>ISO next-search origin. Absent ⇒ promoted (resume strictly after epoch).</summary>
	[JsonPropertyName("cursor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Cursor { get; set; }

	[JsonPropertyName("emittedCount")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? EmittedCount { get; set; }

	[JsonPropertyName("exhausted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Exhausted { get; set; }

	/// <summary><c>@x</c>/<c>*x</c> progress, keyed by node id.</summary>
	[JsonPropertyName("counters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, OrbitCounterState>? Counters { get; set; }

	[JsonPropertyName("autoReset")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? AutoReset { get; set; }

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	/// <summary>Serializes the snapshot into its interchange JSON form.</summary>
	public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

	/// <summary>Parses a snapshot from its interchange JSON form.</summary>
	public static OrbitSnapshot FromJson(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);
		return JsonSerializer.Deserialize<OrbitSnapshot>(json, SerializerOptions)
			?? throw new FormatException("Orbit snapshot JSON deserialized to null.");
	}
}
