using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// Provides the long-running hosted core process used by service managers on Windows and Linux.
/// </summary>
public sealed class PlaintorchCoreService(
	IServiceScopeFactory scopeFactory,
	ILogger<PlaintorchCoreService> logger,
	PlaintorchVaultActivationService activationService,
	VaultLayout layout) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var activeVault = activationService.GetActiveVaultPath() ?? layout.VaultRoot;
		logger.LogInformation("PLAINTORCH core starting for vault '{VaultPath}'.", activeVault);

		using (var scope = scopeFactory.CreateScope())
		{
			var engine = scope.ServiceProvider.GetRequiredService<PlaintorchEngine>();
			await engine.InitializeVaultAsync(stoppingToken);
			logger.LogInformation("PLAINTORCH core initialized vault state for '{VaultPath}'.", activeVault);
		}

		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("PLAINTORCH core stopping for vault '{VaultPath}'.", activeVault);
		}
	}
}