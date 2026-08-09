namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Background pass that materializes the rolling window's due instances so the agenda (unbound attentives
/// requiring attention, upcoming eventives) reflects reality without waiting for a Polaris cycle to be begun.
/// </summary>
/// <remarks>
/// "Due" is the next 24 hours, not the calendar day. The pass is idempotent — occurrences are deduped by
/// identity and the orbit schedule state only advances forward — so it runs on startup and then hourly, and
/// the rolling window keeps the coming hours materialized without pinning to midnight. A failure (for example,
/// the database not yet migrated during startup) is swallowed and retried on the next tick rather than
/// crashing the host.
/// </remarks>
public sealed class RollingMaterializationService(
	IServiceScopeFactory scopeFactory,
	ILogger<RollingMaterializationService> logger) : BackgroundService
{
	private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			// Give the core a moment to finish initializing the vault and migrating the database first.
			await Task.Delay(InitialDelay, stoppingToken);
			await RunOnceAsync(stoppingToken);

			using var timer = new PeriodicTimer(Interval);
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await RunOnceAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
			// The host is shutting down.
		}
	}

	private async Task RunOnceAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var scope = scopeFactory.CreateScope();
			var materialization = scope.ServiceProvider.GetRequiredService<ProximityMaterializationService>();
			var now = DateTimeOffset.Now;
			var created = await materialization.MaterializeForNowAsync(now, ProximityMaterializationService.DefaultEventiveHorizonDays, cancellationToken);
			if (created > 0)
			{
				logger.LogInformation("Rolling materialization created {Created} instance(s) at {Now:yyyy-MM-dd HH:mm}.", created, now);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Rolling materialization pass failed; it will retry on the next tick.");
		}
	}
}
