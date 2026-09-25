global using A11d.Module;
using Pleiades.Calendar;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// WARNING:
/// <br/>
/// This is a marker class and serves no function.
/// </summary>
public sealed class PLAINTORCH { }

/// <summary>
/// Provides the console entry point of the PLAINTORCH core: one-shot vault commands and the long-lived <c>serve</c> host.
/// </summary>
/// <remarks>
/// The composition root lives in <see cref="PlaintorchHostFactory"/>; this class only maps a command line onto host
/// options and runs the selected command against the built host.
/// </remarks>
public static class Program
{
	/// <summary>
	/// Exit code returned when <c>serve</c> finds another core already serving the same profile.
	/// </summary>
	public const int AlreadyRunningExitCode = 3;

	private static readonly string ProgramCategory = typeof(Program).FullName!;

	/// <summary>
	/// Runs the PLAINTORCH command-line shell.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>A process exit code.</returns>
	public static async Task<int> Main(string[] args)
	{
		var launch = PlaintorchLaunchArguments.TryParse(args);
		if (launch is null)
		{
			Console.Error.WriteLine($"Unknown command. Supported commands: {PlaintorchLaunchArguments.SupportedCommands}.");
			return 1;
		}

		var userLayout = launch.ResolveUserLayout();
		var launchMode = launch.ResolveLaunchMode();
		if (launch.IsServe)
		{
			ApplyServeVaultOverride(launch);
			if (await PlaintorchInstanceProbe.TryDetectAsync(userLayout) is { } running)
			{
				ReportAlreadyRunning(running, launchMode);
				return AlreadyRunningExitCode;
			}
		}

		var app = PlaintorchHostFactory.Create(new PlaintorchHostOptions
		{
			UserLayout = userLayout,
			LaunchMode = launchMode,
			VaultPath = launch.ResolveCommandVaultPath(),
			Arguments = args,
		});

		if (launch.IsServe && launchMode != PlaintorchLaunchMode.Spawn)
		{
			RenderServeBanner(userLayout);
		}

		var fileLogs = app.Services.GetServices<ILoggerProvider>().OfType<PlaintorchFileLoggerProvider>().FirstOrDefault();
		if (fileLogs is not null)
		{
			ObserveProcessFailures(fileLogs);
		}

		return launch.Command switch
		{
			"init" => await RunInitializeAsync(app, launch.ResolveCommandVaultPath()!),
			"activate" => RunActivate(app, launch.ResolveCommandVaultPath()!),
			"deactivate" => RunDeactivate(app),
			"serve" => await RunServeAsync(app, launchMode, fileLogs),
			"bootstrap-service" => RunBootstrapService(app),
			_ => 1,
		};
	}

	/// <summary>
	/// Initializes the current working directory as a PLAINTORCH vault when needed.
	/// </summary>
	private static async Task<int> RunInitializeAsync(WebApplication application, string vaultPath)
	{
		using var scope = application.Services.CreateScope();
		var layout = scope.ServiceProvider.GetRequiredService<VaultLayout>();
		var engine = scope.ServiceProvider.GetRequiredService<PlaintorchEngine>();
		var activationService = scope.ServiceProvider.GetRequiredService<PlaintorchVaultActivationService>();
		var alreadyInitialized = activationService.IsInitialized(vaultPath);

		await engine.InitializeVaultAsync();
		RenderHeader(layout);
		Console.WriteLine(alreadyInitialized ? "Vault already initialized." : "Vault initialized.");
		Console.WriteLine($"Database: {layout.DatabasePath}");
		Console.WriteLine(engine.GetStatusReport());
		return 0;
	}

	/// <summary>
	/// Marks the current working directory as the active per-user PLAINTORCH vault.
	/// </summary>
	private static int RunActivate(WebApplication application, string vaultPath)
	{
		using var scope = application.Services.CreateScope();
		var activationService = scope.ServiceProvider.GetRequiredService<PlaintorchVaultActivationService>();
		var userLayout = scope.ServiceProvider.GetRequiredService<PlaintorchUserLayout>();

		try
		{
			var activeVaultPath = activationService.Activate(vaultPath);
			Console.WriteLine("Active vault updated.");
			Console.WriteLine($"Vault: {activeVaultPath}");
			Console.WriteLine($"User Config: {userLayout.ConfigurationPath}");
			Console.WriteLine($"Endpoint: {userLayout.EndpointDisplay}");
			Console.WriteLine($"Loopback API: {userLayout.LoopbackBaseUrl}");
			return 0;
		}
		catch (InvalidOperationException exception)
		{
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Runs the long-lived PLAINTORCH host.
	/// </summary>
	/// <remarks>
	/// The host starts idle without a vault. Vault activation, lock ownership, and vault initialization are
	/// coordinated at runtime by <see cref="PlaintorchCoreService"/> in response to user settings, so that a
	/// vault can be activated and deactivated without restarting the host. In spawn mode a startup failure is
	/// also reported on the status stream before the process exits, so the shell can show it. With file logging
	/// (daemon and spawn modes) the failure, stack trace included, is also written to the profile's day log.
	/// </remarks>
	private static async Task<int> RunServeAsync(WebApplication application, PlaintorchLaunchMode launchMode, PlaintorchFileLoggerProvider? fileLogs)
	{
		try
		{
			await application.RunAsync();
			return 0;
		}
		catch (Exception exception)
		{
			// The host is already disposed here, and its status stream and file sink with it, so the failure is
			// written directly (the disposed sink appends straight to the day file).
			fileLogs?.Append(LogLevel.Critical, ProgramCategory, "The PLAINTORCH core host failed to start or stopped with an error.", exception);
			fileLogs?.Dispose();
			var failure = new PlaintorchHostStatus(
				PlaintorchHostPhase.Failed,
				$"PLAINTORCH core failed to start.{Environment.NewLine}{exception.Message}",
				null,
				DateTimeOffset.UtcNow);
			if (launchMode == PlaintorchLaunchMode.Spawn)
			{
				PlaintorchHostStatusStream.WriteStandalone(PlaintorchHostStatusStream.ToEvent(failure));
			}
			else
			{
				Console.Error.WriteLine(exception.Message);
			}

			return 1;
		}
	}

	/// <summary>
	/// Records process-level failures of a file-logging host in its day log as critical entries, stack traces
	/// included: an exception that escaped onto any thread, after which the runtime terminates the process, so the
	/// sink is drained and flushed before the handler returns; and a faulted task that nobody observed, which the
	/// process survives, so the entry is flushed with the sink's next batch.
	/// </summary>
	/// <remarks>
	/// Both go through <see cref="PlaintorchFileLoggerProvider.Append"/>, which queues while the host is running and
	/// appends straight to the file once the host, and the sink with it, has been disposed.
	/// </remarks>
	private static void ObserveProcessFailures(PlaintorchFileLoggerProvider fileLogs)
	{
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			var exception = args.ExceptionObject as Exception;
			var message = exception is null
				? $"Unhandled exception ({args.ExceptionObject}); the PLAINTORCH core is terminating."
				: "Unhandled exception; the PLAINTORCH core is terminating.";
			fileLogs.Append(LogLevel.Critical, ProgramCategory, message, exception);
			if (args.IsTerminating)
			{
				fileLogs.Dispose();
			}
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
			fileLogs.Append(LogLevel.Critical, ProgramCategory, "A faulted task was never observed.", args.Exception);
	}

	/// <summary>
	/// Clears the active per-user PLAINTORCH vault, returning any running core to idle.
	/// </summary>
	private static int RunDeactivate(WebApplication application)
	{
		using var scope = application.Services.CreateScope();
		var activationService = scope.ServiceProvider.GetRequiredService<PlaintorchVaultActivationService>();
		var userLayout = scope.ServiceProvider.GetRequiredService<PlaintorchUserLayout>();

		var previousVaultPath = activationService.Deactivate();
		if (previousVaultPath is null)
		{
			Console.WriteLine("No active vault was configured.");
		}
		else
		{
			Console.WriteLine("Active vault cleared.");
			Console.WriteLine($"Previous Vault: {previousVaultPath}");
		}

		Console.WriteLine($"User Config: {userLayout.ConfigurationPath}");
		return 0;
	}

	/// <summary>
	/// Generates headless service bootstrap assets for the current operating system.
	/// </summary>
	private static int RunBootstrapService(WebApplication application)
	{
		using var scope = application.Services.CreateScope();
		var bootstrapper = scope.ServiceProvider.GetRequiredService<PlaintorchServiceBootstrapper>();
		try
		{
			var result = bootstrapper.Bootstrap();
			Console.WriteLine("Service bootstrap assets generated.");
			Console.WriteLine($"Root: {result.RootPath}");
			foreach (var file in result.GeneratedFiles)
			{
				Console.WriteLine($"- {file}");
			}

			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Tells the launcher that another core already owns the profile. In spawn mode the shell reads it as a
	/// status line; otherwise it is plain console output.
	/// </summary>
	private static void ReportAlreadyRunning(PlaintorchInstanceProbe.RunningInstance running, PlaintorchLaunchMode launchMode)
	{
		if (launchMode == PlaintorchLaunchMode.Spawn)
		{
			PlaintorchHostStatusStream.WriteStandalone(new
			{
				@event = "already-running",
				endpoint = running.Endpoint,
				mode = running.Mode,
				vault = running.ActiveVault,
			});
			return;
		}

		var vaultSuffix = running.ActiveVault is null ? string.Empty : $", vault {running.ActiveVault}";
		Console.Error.WriteLine($"A PLAINTORCH core is already serving this profile at {running.Endpoint} ({running.Mode}{vaultSuffix}).");
	}

	/// <summary>
	/// Writes the serve banner describing the bound endpoint and profile.
	/// </summary>
	private static void RenderServeBanner(PlaintorchUserLayout userLayout)
	{
		Console.WriteLine($"PLAINTORCH endpoint: {userLayout.EndpointDisplay}");
		if (userLayout.LoopbackEnabled)
		{
			Console.WriteLine($"PLAINTORCH loopback: {userLayout.LoopbackBaseUrl}");
		}

		Console.WriteLine($"PLAINTORCH logs: {userLayout.LogsRootPath}");
		if (userLayout.IsEphemeral)
		{
			Console.WriteLine("PLAINTORCH environment: ephemeral (dev)");
		}
		else if (userLayout.IsDevProfile)
		{
			Console.WriteLine("PLAINTORCH environment: dev sub-profile (persistent; pass --daemon to run the real per-user profile)");
		}
	}

	/// <summary>
	/// Writes a short banner describing the current vault and calendar date.
	/// </summary>
	private static void RenderHeader(VaultLayout layout)
	{
		var today = PleiadeanCalendar.FromDateTime(DateTime.Today);
		Console.WriteLine("PLAINTORCH");
		Console.WriteLine($"Vault Root: {layout.VaultRoot}");
		Console.WriteLine($"Pleiadean Today: {today}");
		Console.WriteLine();
	}

	/// <summary>
	/// Applies an explicit <c>serve --vault &lt;path&gt;</c> as a direct runtime vault target. A manual/dev serve is
	/// meant to serve a specific vault without editing user configuration, so the argument is surfaced through the same
	/// <c>PLAINTORCH_VAULT_PATH</c> channel that <see cref="PlaintorchCoreService"/> already prefers over the persisted
	/// active-vault setting — the config is ignored, exactly as a sandbox serve expects.
	/// </summary>
	private static void ApplyServeVaultOverride(PlaintorchLaunchArguments launch)
	{
		if (string.IsNullOrWhiteSpace(launch.VaultArgument))
		{
			return;
		}

		Environment.SetEnvironmentVariable("PLAINTORCH_VAULT_PATH", Path.GetFullPath(launch.VaultArgument));
	}
}
