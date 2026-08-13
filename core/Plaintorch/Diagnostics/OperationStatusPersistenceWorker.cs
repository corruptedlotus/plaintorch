using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Diagnostics;

/// <summary>
/// Periodically drains buffered operation-status transitions (PEP108) to the vault database, in its own scope, so
/// persistence is decoupled from the call sites that report status (which may run outside any DI scope).
/// </summary>
public sealed class OperationStatusPersistenceWorker(
	IServiceScopeFactory scopeFactory,
	OperationStatusEventBuffer buffer,
	ILogger<OperationStatusPersistenceWorker> logger) : BackgroundService
{
	private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(FlushInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await FlushAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
			// Shutting down; fall through to a final flush.
		}

		await FlushAsync(CancellationToken.None);
	}

	private async Task FlushAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var scope = scopeFactory.CreateScope();
			var context = scope.ServiceProvider.GetRequiredService<PlainfraContext>();
			await buffer.DrainAsync(context, cancellationToken);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Failed to persist buffered operation-status transitions. Will retry on the next flush.");
		}
	}
}
