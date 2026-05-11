using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// Provides the long-running hosted core process used by service managers on Windows and Linux.
/// </summary>
public sealed class PlaintorchCoreService(
	IServiceScopeFactory scopeFactory,
	ILogger<PlaintorchCoreService> logger,
	PlaintorchVaultActivationService activationService,
	VaultLayout layout,
	PlaintorchCoreSplashService splashService) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var activeVault = activationService.GetActiveVaultPath() ?? layout.VaultRoot;
		logger.LogInformation("PLAINTORCH core starting for vault '{VaultPath}'.", activeVault);

		try
		{
			await splashService.ShowLoadingAsync("Loading vault state...");

			using (var scope = scopeFactory.CreateScope())
			{
				var engine = scope.ServiceProvider.GetRequiredService<PlaintorchEngine>();
				await splashService.ShowLoadingAsync("Initializing vault layout, database, and indexes...");
				await engine.InitializeVaultAsync(stoppingToken);
				logger.LogInformation("PLAINTORCH core initialized vault state for '{VaultPath}'.", activeVault);
			}

			await splashService.ShowLoadingAsync("Core ready. Starting background runtime...");
			await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
			await splashService.CloseAsync(stoppingToken);
			await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
		}
		catch (OperationCanceledException)
		{
			await splashService.CloseAsync();
			logger.LogInformation("PLAINTORCH core stopping for vault '{VaultPath}'.", activeVault);
		}
		catch (Exception exception)
		{
			await splashService.ShowErrorAsync("PLAINTORCH core initialization failed.", exception, stoppingToken);
			logger.LogError(exception, "PLAINTORCH core failed to initialize for vault '{VaultPath}'.", activeVault);
			throw;
		}
	}
}