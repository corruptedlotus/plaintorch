namespace Pleiades.Puck;

/// <summary>
/// Describes the supported numerator strategies inside PUCK declarations.
/// </summary>
public enum PuckNumeratorKind
{
	/// <summary>
	/// Requires the caller to provide the numerator explicitly.
	/// </summary>
	Manual,
	/// <summary>
	/// Uses a randomly generated numeric spiritgem.
	/// </summary>
	Spiritgem,
	/// <summary>
	/// Uses a persisted incremental sequence.
	/// </summary>
	Incremental,
	/// <summary>
	/// Uses a date stamp in <c>yyyyMMdd</c> format.
	/// </summary>
	DateStamp,
}

/// <summary>
/// Describes how adjacent PUCK segments are joined.
/// </summary>
public enum PuckNestingKind
{
	/// <summary>
	/// No nesting separator is applied.
	/// </summary>
	None,
	/// <summary>
	/// Segments are joined using telescope nesting.
	/// </summary>
	Telescope,
	/// <summary>
	/// Segments are joined using filesystem nesting.
	/// </summary>
	Filesystem,
}

/// <summary>
/// Describes supported repetition markers inside a PUCK declaration.
/// </summary>
public enum PuckRepetitionMode
{
	/// <summary>
	/// No repetition is applied.
	/// </summary>
	None,
	/// <summary>
	/// Repeats the previous segment.
	/// </summary>
	RepeatPreviousSegment,
	/// <summary>
	/// Repeats the entire pattern.
	/// </summary>
	RepeatWholePattern,
}

/// <summary>
/// Describes the numerator portion of a PUCK segment.
/// </summary>
public sealed record PuckNumeratorPattern(PuckNumeratorKind Kind, int Width = 0, long Seed = 0);

/// <summary>
/// Describes a single PUCK segment pattern.
/// </summary>
public sealed record PuckSegmentPattern(
	string? StaticDiscriminator,
	bool UsesDynamicDiscriminator,
	PuckNumeratorPattern Numerator,
	PuckNestingKind Nesting,
	bool ForceNesting,
	PuckRepetitionMode Repetition);

/// <summary>
/// Represents a parsed PUCK declaration.
/// </summary>
public sealed record PuckNotation(IReadOnlyList<PuckSegmentPattern> Segments);

/// <summary>
/// Supplies caller input used while generating a concrete PUCK identifier.
/// </summary>
public sealed record PuckSegmentInput(string? Discriminator = null, string? Numerator = null, DateOnly? Date = null);
