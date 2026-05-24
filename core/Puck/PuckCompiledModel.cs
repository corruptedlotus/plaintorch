namespace Pleiades.Puck;

/// <summary>
/// Represents a runtime-compiled PUCK model for a specific entity type.
/// </summary>
/// <param name="EntityType">The entity CLR type.</param>
/// <param name="Declaration">The raw PUCK declaration string.</param>
/// <param name="Notation">The parsed PUCK notation tree.</param>
/// <param name="RequiresCallerInput">Indicates whether caller input is required to create identifiers.</param>
public sealed record PuckCompiledModel(
	Type EntityType,
	string Declaration,
	PuckNotation Notation,
	bool RequiresCallerInput)
{
	/// <summary>
	/// Gets the first PUCK segment pattern, when available.
	/// </summary>
	public PuckSegmentPattern? FirstSegment => Notation.Segments.FirstOrDefault();
}
