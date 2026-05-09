using System.ComponentModel.DataAnnotations;
namespace Pleiades.Puck;

/// <summary>
/// Records a previously issued PUCK identifier and the declaration that produced it.
/// </summary>
public sealed class PuckRegistryEntry
{
	[Key]
	/// <summary>
	/// Gets or sets the issued identifier.
	/// </summary>
	public required string Id { get; set; }

	/// <summary>
	/// Gets or sets the declaration used to generate the identifier.
	/// </summary>
	public required string Declaration { get; set; }

	/// <summary>
	/// Gets or sets the UTC instant when the identifier was issued.
	/// </summary>
	public DateTimeOffset IssuedUtc { get; set; }
}