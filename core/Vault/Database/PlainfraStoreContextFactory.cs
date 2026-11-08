using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Creates bare <see cref="PlainfraContext"/>s over the active vault's database, for writes that belong to no domain unit
/// of work — PUCK issuance. A bare context opens the same store, with the same connection pragmas, as every context, and
/// carries none of the domain save hooks (state policy, backlog schedules, change feed, caches): it saves exactly what it
/// was given, and never anything another context has pending.
/// </summary>
/// <remarks>
/// Each bare context is its own connection to the one database file, which only works because no caller holds a write
/// transaction open while it asks for one; SQLite allows a single writer, so a bare write would otherwise wait out the
/// busy timeout behind its own caller.
/// </remarks>
public sealed class PlainfraStoreContextFactory(VaultLayout layout)
{
	/// <summary>
	/// Creates a bare context over the active vault's database. The caller owns and disposes it.
	/// </summary>
	public PlainfraContext Create()
	{
		var options = new DbContextOptionsBuilder<PlainfraContext>();
		ConfigureStore(options, layout);
		return new PlainfraContext(options.Options);
	}

	/// <summary>
	/// Points a context at the store every PLAINTORCH context opens: the vault's SQLite database, with WAL and a busy timeout
	/// asserted on each connection (<see cref="SqlitePragmaConnectionInterceptor"/>). The one definition the request
	/// contexts and the bare ones share.
	/// </summary>
	public static void ConfigureStore(DbContextOptionsBuilder options, VaultLayout layout)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(layout);
		Directory.CreateDirectory(layout.MetadataRoot);
		options.UseSqlite($"Data Source={layout.DatabasePath}");
		// Stateless, so a fresh instance per context.
		options.AddInterceptors(new SqlitePragmaConnectionInterceptor());
	}
}
