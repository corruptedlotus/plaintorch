using A11d.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Vault;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A configured vault that is not initialized leaves the core idle and reports <see cref="PlaintorchHostPhase.Failed"/>;
/// once the folder is initialized — as the desktop shell does to a vault it is asked to activate — the coordinator
/// serves it at the next reconcile, without the configured path changing or the core restarting. Before, a path that
/// had failed for not being initialized was never tried again.
/// </summary>
[Collection(CoordinatorEnvironmentCollection.Name)]
public sealed class UninitializedVaultActivationTests
{
	private const string NotInitializedMessage = "The configured vault is not initialized for PLAINTORCH.";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task An_uninitialized_vault_is_served_once_it_is_initialized()
	{
		var vaultRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "plaintorch-tests", $"uninitialized-{Guid.NewGuid():N}"));
		Directory.CreateDirectory(vaultRoot);

		// A daemon exactly as `serve` starts one: no vault bound, activation driven by the coordinator.
		var userLayout = PlaintorchUserLayout.CreateEphemeral();
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddSingleton(userLayout);
		await using var daemon = builder.Install<PLAINTORCH>().Build();

		var hostState = daemon.Services.GetRequiredService<PlaintorchHostState>();
		var session = daemon.Services.GetRequiredService<ActiveVaultSession>();
		var coordinator = daemon.Services.GetServices<IHostedService>().OfType<PlaintorchCoreService>().Single();
		var refused = new TaskCompletionSource<PlaintorchHostStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
		var served = new TaskCompletionSource<PlaintorchHostStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
		hostState.Changed += status =>
		{
			if (status.Phase is PlaintorchHostPhase.Failed)
			{
				refused.TrySetResult(status);
			}
			else if (status.Phase is PlaintorchHostPhase.Active)
			{
				served.TrySetResult(status);
			}
		};

		// See DatabaseAheadOfCoreTests: the override wins over the profile's config, so it is what points the daemon here.
		var previousOverride = Environment.GetEnvironmentVariable("PLAINTORCH_VAULT_PATH");
		Environment.SetEnvironmentVariable("PLAINTORCH_VAULT_PATH", vaultRoot);
		PlaintorchHostStatus refusal;
		PlaintorchHostStatus service;
		try
		{
			await coordinator.StartAsync(Ct);
			refusal = await refused.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
			Assert.False(session.IsActive);

			// What `plaintorch init` writes first; the coordinator's activation does the rest of the initialization.
			new VaultSettings().Save(Path.Combine(vaultRoot, VaultOptions.DefaultSettingsFileName));
			service = await served.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

			// Active is reported once the activation has initialized the vault (and just before the session is marked
			// ready, so the session itself is not asserted here): the database is there.
			Assert.True(File.Exists(Path.Combine(vaultRoot, VaultOptions.DefaultMetadataDirectoryName, VaultOptions.DefaultDatabaseFileName)));
		}
		finally
		{
			await coordinator.StopAsync(CancellationToken.None);
			Environment.SetEnvironmentVariable("PLAINTORCH_VAULT_PATH", previousOverride);
			userLayout.Cleanup();
			SqliteConnection.ClearAllPools();
			TryDeleteDirectory(vaultRoot);
		}

		Assert.Equal(PlaintorchHostPhase.Failed, refusal.Phase);
		Assert.Equal(NotInitializedMessage, refusal.Message);
		Assert.Equal(vaultRoot, refusal.VaultPath);

		Assert.Equal(PlaintorchHostPhase.Active, service.Phase);
		Assert.Equal(vaultRoot, service.VaultPath);
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}

/// <summary>
/// The tests that drive <see cref="PlaintorchCoreService"/> through the process-wide <c>PLAINTORCH_VAULT_PATH</c>. They
/// share this collection so they never run in parallel and point one another's daemons at the wrong vault.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CoordinatorEnvironmentCollection
{
	/// <summary>The collection's name.</summary>
	public const string Name = "Coordinator environment";
}
