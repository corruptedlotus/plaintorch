namespace Pleiades.Puck;

/// <summary>
/// Declares the PUCK notation used to issue identifiers for a model type.
/// Not inherited, so sibling types in a shared-table hierarchy each declare their own notation explicitly.
/// </summary>
/// <param name="notation">The PUCK notation string.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PuckFormatAttribute(string notation) : Attribute
{
	/// <summary>
	/// Gets the PUCK notation string.
	/// </summary>
	public string Notation { get; } = notation;
}
