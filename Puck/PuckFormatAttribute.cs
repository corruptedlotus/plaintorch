namespace Pleiades.Puck;

/// <summary>
/// Declares the PUCK notation used to issue identifiers for a model type.
/// </summary>
/// <param name="notation">The PUCK notation string.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class PuckFormatAttribute(string notation) : Attribute
{
	/// <summary>
	/// Gets the PUCK notation string.
	/// </summary>
	public string Notation { get; } = notation;
}
