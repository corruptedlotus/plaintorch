using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pleiades.Vault;

namespace Pleiades.Vault.Database;

/// <summary>
/// Creates configured <see cref="PlainfraContext"/> instances for design-time EF Core operations.
/// </summary>
public sealed class PlainfraContextDesignTimeFactory : IDesignTimeDbContextFactory<PlainfraContext>
{
	/// <summary>
	/// Creates a database context for design-time EF Core tooling.
	/// </summary>
	/// <param name="args">The design-time arguments.</param>
	/// <returns>A configured context instance.</returns>
	public PlainfraContext CreateDbContext(string[] args)
	{
		var vaultPath = ResolveVaultPath(args);
		var options = new VaultOptions
		{
			VaultPath = vaultPath,
		};

		var layout = new VaultLayout(options);
		Directory.CreateDirectory(Path.GetDirectoryName(layout.DatabasePath)!);

		var dbContextOptions = new DbContextOptionsBuilder<PlainfraContext>()
			.UseSqlite($"Data Source={layout.DatabasePath}")
			.Options;

		return new PlainfraContext(dbContextOptions);
	}

	private static string ResolveVaultPath(IReadOnlyList<string> args)
	{
		for (var index = 0; index < args.Count - 1; index++)
		{
			if (string.Equals(args[index], "--vault", StringComparison.OrdinalIgnoreCase))
			{
				return args[index + 1];
			}
		}

		return Environment.GetEnvironmentVariable("PLAINTORCH_VAULT_PATH")
			?? Directory.GetCurrentDirectory();
	}
}