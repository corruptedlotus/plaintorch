using Pleiades.Resources;

namespace Pleiades.Vault.Database;

/// <summary>
/// Raised at vault activation when the vault's database records EF Core migrations this core does not contain: a newer
/// core (or a divergent build) migrated it. <c>Migrate</c> would apply nothing and the core would serve a model that no
/// longer matches the schema, so every query touching a changed table would fail instead. Activation is refused before
/// the database is written to.
/// </summary>
public sealed class VaultDatabaseAheadOfCoreException : InvalidOperationException
{
	/// <summary>
	/// Initializes the exception for a vault whose database carries the given unknown migrations.
	/// </summary>
	/// <param name="vaultPath">The root of the refused vault.</param>
	/// <param name="unknownMigrations">The applied migration ids this core does not contain, oldest first; at least one.</param>
	public VaultDatabaseAheadOfCoreException(string vaultPath, IReadOnlyList<string> unknownMigrations)
		: base(VaultMessages.Activation.DatabaseAheadOfCore(vaultPath, unknownMigrations[^1]))
	{
		VaultPath = vaultPath;
		UnknownMigrations = unknownMigrations;
	}

	/// <summary>Gets the root of the refused vault.</summary>
	public string VaultPath { get; }

	/// <summary>Gets the applied migration ids this core does not contain, oldest first.</summary>
	public IReadOnlyList<string> UnknownMigrations { get; }

	/// <summary>Gets the newest migration id this core does not contain (the one the message names).</summary>
	public string NewestUnknownMigration => UnknownMigrations[^1];
}
