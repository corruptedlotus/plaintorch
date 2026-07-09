namespace Pleiades.Vault.Migration;

/// <summary>
/// Summarizes the effect of applying a single vault migration.
/// </summary>
/// <param name="Loaded">The number of entities reconstructed from disk under the source conventions.</param>
/// <param name="Rewritten">The number of entities re-canonicalised to the current conventions.</param>
/// <param name="Archived">The number of legacy files snapshotted to the graveyard before rewrite.</param>
/// <param name="Imported">The number of loaded entities that were not present in the database and were created.</param>
/// <param name="Conflicts">The number of entities that could not be reconciled and require manual attention.</param>
public sealed record VaultMigrationOutcome(int Loaded, int Rewritten, int Archived, int Imported, int Conflicts);
