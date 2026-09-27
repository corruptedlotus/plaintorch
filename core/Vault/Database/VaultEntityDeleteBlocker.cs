namespace Pleiades.Vault.Database;

/// <summary>
/// One restricting relationship that still holds rows referencing an entity, so the database would refuse to delete it:
/// a foreign key that neither cascades nor nulls on delete (for example an executive record's objective, or a child
/// objective's directive). Found by <see cref="VaultEntityGateway.FindDeleteBlockersAsync"/> from the model's own
/// relationship metadata, never a per-type rule list.
/// </summary>
/// <param name="DependentEntity">The CLR name of the referencing entity (for example <c>Executive</c>).</param>
/// <param name="ForeignKeyProperty">The referencing foreign-key property (for example <c>IncentiveId</c>).</param>
/// <param name="Count">How many rows still reference the entity through it.</param>
public sealed record VaultEntityDeleteBlocker(string DependentEntity, string ForeignKeyProperty, int Count);
