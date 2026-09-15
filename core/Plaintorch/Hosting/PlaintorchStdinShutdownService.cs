namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// Requests a graceful host shutdown when the parent process closes this process's standard input.
/// </summary>
/// <remarks>
/// A desktop shell that spawned the core cannot signal it portably: a Node <c>kill()</c> on Windows is a hard
/// terminate, which would leave the vault lock held and unflushed status events lost. Closing the stdin pipe, which
/// every platform does when the parent quits or asks for it, is the cross-platform equivalent of SIGTERM here.
/// Only registered in <see cref="PlaintorchLaunchMode.Spawn"/>.
/// </remarks>
public sealed class PlaintorchStdinShutdownService(
	IHostApplicationLifetime lifetime,
	ILogger<PlaintorchStdinShutdownService> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!Console.IsInputRedirected)
		{
			logger.LogWarning("Standard input is not redirected; stdin-close shutdown is unavailable for this spawn.");
			return Task.CompletedTask;
		}

		// A dedicated thread: the blocking read has no cancellable async form on every platform, and it must not
		// pin a thread-pool thread for the lifetime of the host.
		var thread = new Thread(() => WatchForEndOfInput(stoppingToken))
		{
			IsBackground = true,
			Name = "plaintorch-stdin-watch",
		};
		thread.Start();
		return Task.CompletedTask;
	}

	private void WatchForEndOfInput(CancellationToken stoppingToken)
	{
		try
		{
			using var input = Console.OpenStandardInput();
			var buffer = new byte[256];
			while (!stoppingToken.IsCancellationRequested)
			{
				var read = input.Read(buffer, 0, buffer.Length);
				if (read <= 0)
				{
					break;
				}
			}
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
		}

		if (stoppingToken.IsCancellationRequested)
		{
			return;
		}

		logger.LogInformation("Standard input closed by the parent process. Stopping the PLAINTORCH core.");
		lifetime.StopApplication();
	}
}
