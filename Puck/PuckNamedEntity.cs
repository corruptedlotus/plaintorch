using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Puck;

/// <summary>
/// Represents an entity whose PUCK token and title form its public identity.
/// </summary>
public interface IPuckNamedEntity
{
	/// <summary>
	/// Gets or sets the PUCK token.
	/// </summary>
	string Id { get; set; }

	/// <summary>
	/// Gets or sets the human-readable title.
	/// </summary>
	string Title { get; set; }
}

/// <summary>
/// Provides a shared PUCK token and title identity for PUCK-powered entities.
/// </summary>
public abstract class PuckNamedEntity : IPuckNamedEntity
{
	/// <summary>
	/// Gets or sets the PUCK token.
	/// </summary>
	[Key]
	public required string Id { get; set; }

	/// <summary>
	/// Gets or sets the human-readable title.
	/// </summary>
	public required string Title { get; set; }
}