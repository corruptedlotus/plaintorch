using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Ensures the vault-local database schema exists before the application begins normal work.
/// </summary>
public sealed class PlainfraContextInitializer(PlainfraContext context)
{
	/// <summary>
	/// Applies pending database migrations before the application begins normal work.
	/// </summary>
	public void Initialize()
	{
		context.Database.Migrate();
	}

	/// <summary>
	/// Applies pending database migrations before the application begins normal work.
	/// </summary>
	public Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		return context.Database.MigrateAsync(cancellationToken);
	}
}