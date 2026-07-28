namespace Pleiades.Puck;

/// <summary>
/// Represents the resolution outcome for a PUCK identifier.
/// </summary>
/// <param name="Id">The resolved PUCK identifier.</param>
/// <param name="Exists">Indicates whether an entity exists for the identifier.</param>
/// <param name="EntityType">The resolved entity CLR type name when found.</param>
/// <param name="EntityKind">The resolved stable entity kind declared by the entity via <see cref="PuckEntityAttribute"/> when found.</param>
/// <param name="Entity">The resolved entity instance when found.</param>
/// <param name="AssociatedNote">The vault-relative associated markdown note path when one exists.</param>
public sealed record PuckEntityExistence(
	string Id,
	bool Exists,
	string? EntityType = null,
	string? EntityKind = null,
	object? Entity = null,
	string? AssociatedNote = null);
