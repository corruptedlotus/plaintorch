namespace Pleiades.Puck;

/// <summary>
/// Represents the baseline tokenization of a concrete PUCK value derived from its declaration.
/// </summary>
/// <param name="RawValue">The original PUCK string.</param>
/// <param name="Notation">The declaration that defined the tokenization shape.</param>
/// <param name="Segments">The tokenized PUCK segments.</param>
public sealed record PuckTokenization(string RawValue, PuckNotation Notation, IReadOnlyList<PuckSegmentToken> Segments);

/// <summary>
/// Represents a single PUCK segment token derived from the declaration and concrete value.
/// </summary>
/// <param name="Index">The zero-based segment index.</param>
/// <param name="RawValue">The original segment text.</param>
/// <param name="Discriminator">The resolved discriminator, if any.</param>
/// <param name="Numerator">The resolved numerator text.</param>
/// <param name="NumeratorKind">The numerator kind declared by the notation.</param>
/// <param name="NextNesting">The nesting that follows this segment in the notation.</param>
/// <param name="ForceNesting">Indicates whether nesting is forced by the declaration.</param>
/// <param name="Repetition">The repetition mode that follows this segment in the declaration.</param>
/// <param name="NumericValue">The parsed numeric value when the numerator is numeric.</param>
/// <param name="DateValue">The parsed date when the numerator is a PUCK date stamp.</param>
public sealed record PuckSegmentToken(
	int Index,
	string RawValue,
	string? Discriminator,
	string Numerator,
	PuckNumeratorKind NumeratorKind,
	PuckNestingKind NextNesting,
	bool ForceNesting,
	PuckRepetitionMode Repetition,
	long? NumericValue = null,
	DateOnly? DateValue = null);

/// <summary>
/// Identifies the baseline token field used by a semantic projection mapping.
/// </summary>
public enum PuckSemanticValueSource
{
	/// <summary>
	/// Uses the raw segment text.
	/// </summary>
	RawValue,

	/// <summary>
	/// Uses the resolved discriminator.
	/// </summary>
	Discriminator,

	/// <summary>
	/// Uses the resolved numerator text.
	/// </summary>
	Numerator,

	/// <summary>
	/// Uses the resolved numeric numerator value.
	/// </summary>
	NumericValue,

	/// <summary>
	/// Uses the resolved date value for date-stamp numerators.
	/// </summary>
	DateValue,
}

/// <summary>
/// Maps a baseline PUCK token field into a semantic projection key.
/// </summary>
/// <param name="Key">The semantic key to populate.</param>
/// <param name="SegmentIndex">The source segment index.</param>
/// <param name="Source">The baseline token field to project.</param>
public sealed record PuckSemanticMapping(string Key, int SegmentIndex, PuckSemanticValueSource Source);

/// <summary>
/// Represents a semantic projection built from baseline PUCK tokens.
/// </summary>
/// <param name="RawValue">The original PUCK string.</param>
/// <param name="Tokenization">The baseline tokenization used to build the projection.</param>
/// <param name="Values">The projected semantic values.</param>
public sealed record PuckSemanticProjection(
	string RawValue,
	PuckTokenization Tokenization,
	IReadOnlyDictionary<string, object?> Values);