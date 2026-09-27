using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pleiades.Orbits;

// C# port of the @pleiades/orbits snapshot contracts. The JSON shape matches the
// TypeScript OrbitSnapshot exactly so blobs are interchangeable between engines.

/// <summary>
/// The per-node running counters that engines kept for <c>@x</c>/<c>*x</c> before the limits became structural. Kept
/// only so an old snapshot still reads: a snapshot is never written with counters, and they are ignored on resume.
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
/// stream — every limit is a pure function of the three — and the cursor records how far
/// it has progressed.
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

	/// <summary>
	/// Legacy running counters from the engines that counted <c>@x</c>/<c>*x</c> as they went: never written, and
	/// ignored on resume — except that a snapshot carrying them has its exhaustion re-derived, since those counters
	/// could end a stream the structural limits do not.
	/// </summary>
	[JsonPropertyName("counters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, OrbitCounterState>? Counters { get; set; }

	/// <summary>Legacy epoch-rebasing flag: never written, and ignored on resume.</summary>
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
