using Pleiades.Vault.Database;
using Pleiades.Puck;

namespace Pleiades.Vault;

/// <summary>
/// Creates the required filesystem layout and initializes the vault-local database.
/// </summary>
public sealed class VaultBootstrapper(
	VaultLayout layout,
	PlainfraContext context,
	PlainfraContextInitializer contextInitializer,
	VaultStorageTopologyValidator topologyValidator,
	PuckRuntimeCompilationCatalog puckRuntimeCompilationCatalog)
{
	/// <summary>
	/// Ensures required directories and database schema are present.
	/// </summary>
	public void Initialize()
	{
		foreach (var directory in layout.GetRequiredDirectories())
		{
			Directory.CreateDirectory(directory);
		}

		layout.EnsureSettingsFile();
		topologyValidator.Validate();
		CompilePuckModels();

		contextInitializer.Initialize();
	}

	/// <summary>
	/// Ensures required directories and database schema are present.
	/// </summary>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		foreach (var directory in layout.GetRequiredDirectories())
		{
			Directory.CreateDirectory(directory);
		}

		layout.EnsureSettingsFile();
		topologyValidator.Validate();
		CompilePuckModels();
		await contextInitializer.InitializeAsync(cancellationToken);
	}

	private void CompilePuckModels()
	{
		puckRuntimeCompilationCatalog.CompileForActiveVault(context.Model);
	}
}