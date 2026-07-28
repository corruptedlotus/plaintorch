namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Describes what happened to an entity.
/// </summary>
public enum EntityChangeOperation
{
	/// <summary>The entity came into existence.</summary>
	Added,

	/// <summary>The entity was altered.</summary>
	Modified,

	/// <summary>The entity ceased to exist.</summary>
	Deleted,
}

/// <summary>
/// Announces that one entity changed.
/// </summary>
/// <remarks>
/// Carries identity only. A consumer already knows how to resolve an identity, and shipping identities
/// rather than payloads keeps the feed independent of the shape of any API contract.
/// </remarks>
/// <param name="Type">The runtime type name of the entity, matching what the API serializes as <c>@type</c>.</param>
/// <param name="Id">The PUCK token of the entity.</param>
/// <param name="Operation">What happened to the entity.</param>
public readonly record struct EntityChange(string Type, string Id, EntityChangeOperation Operation);
