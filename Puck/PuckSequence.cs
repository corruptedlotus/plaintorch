using System.ComponentModel.DataAnnotations;
namespace Pleiades.Puck;

/// <summary>
/// Stores incremental sequence state for a PUCK declaration.
/// </summary>
public sealed class PuckSequence
{
	[Key]
	/// <summary>
	/// Gets or sets the declaration key.
	/// </summary>
	public required string Key { get; set; }

	/// <summary>
	/// Gets or sets the next numeric value to issue.
	/// </summary>
	public long NextValue { get; set; }
}