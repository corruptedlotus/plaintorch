global using A11d.Module;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Pleiades.Calendar;
using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// WARNING:
/// <br/>
/// This is a marker class and serves no function.
/// </summary>
public sealed class PLAINTORCH { }

/// <summary>
/// Provides the console entry point for initializing and exercising the PLAINTORCH workspace.
/// </summary>
public static class Program
{
	/// <summary>
	/// Runs the PLAINTORCH command-line shell.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns>A process exit code.</returns>
	public static async Task<int> Main(string[] args)
	{
		var command = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant() ?? "init";
		if (command is not ("init" or "activate" or "serve" or "bootstrap-service"))
		{
			Console.Error.WriteLine("Unknown command. Supported commands: init, activate, serve, bootstrap-service.");
			return 1;
		}

		var userLayout = PlaintorchUserLayout.CreateDefault();
		var configurationStore = new PlaintorchUserConfigurationStore(userLayout);
		var vaultPath = ResolveVaultPath(command, args, configurationStore);

		var builder = WebApplication.CreateBuilder(args);
		builder.Host.UseWindowsService();
		builder.Host.UseSystemd();
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
		});
		builder.Services.AddSingleton(new VaultOptions
		{
			VaultPath = vaultPath,
		});
		builder.WebHost.ConfigureKestrel(options =>
		{
			userLayout.EnsureExists();
			TryDeleteStaleSocket(userLayout.SocketPath);
			options.ListenLocalhost(userLayout.LoopbackPort, listenOptions =>
			{
				listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
			});
			options.ListenUnixSocket(userLayout.SocketPath, listenOptions =>
			{
				listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
			});
		});

		var app = builder.Install<PLAINTORCH>().Build();
		app.UseRouting();
		app.Configure<PLAINTORCH>();

		switch (command)
		{
			case "init":
				return await RunInitializeAsync(app, vaultPath);

			case "activate":
				return RunActivate(app, vaultPath);

			case "serve":
				return await RunServeAsync(app);

			case "bootstrap-service":
				return RunBootstrapService(app);

			default:
					return 1;
		}
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
			Console.WriteLine($"Socket: {userLayout.SocketPath}");
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
	/// Runs the long-lived PLAINTORCH host for Windows service or systemd execution.
	/// </summary>
	private static async Task<int> RunServeAsync(WebApplication application)
	{
		await using var scope = application.Services.CreateAsyncScope();
		var lockService = scope.ServiceProvider.GetRequiredService<PlaintorchVaultLockService>();
		var splashService = scope.ServiceProvider.GetRequiredService<PlaintorchCoreSplashService>();

		try
		{
			await splashService.ShowLoadingAsync("Acquiring vault lock...");
			await using var lockHandle = await lockService.AcquireAsync();
			await splashService.ShowLoadingAsync("Starting PLAINTORCH core...");
			await application.RunAsync();
			await splashService.CloseAsync();
			return 0;
		}
		catch (Exception exception)
		{
			await splashService.ShowErrorAsync("PLAINTORCH core failed to start.", exception);
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	private static void TryDeleteStaleSocket(string socketPath)
	{
		try
		{
			if (File.Exists(socketPath))
			{
				File.Delete(socketPath);
			}
		}
		catch (SocketException)
		{
		}
		catch (IOException)
		{
		}
	}

	/// <summary>
	/// Generates service bootstrap assets for the current operating system.
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
	/// Resolves the vault path from command-line arguments or environment variables.
	/// </summary>
	private static string ResolveVaultPath(string command, IReadOnlyList<string> args, PlaintorchUserConfigurationStore configurationStore)
	{
		for (var index = 0; index < args.Count - 1; index++)
		{
			if (string.Equals(args[index], "--vault", StringComparison.OrdinalIgnoreCase))
			{
				return args[index + 1];
			}
		}

		if (command is "init" or "activate" or "bootstrap-service")
		{
			return Directory.GetCurrentDirectory();
		}

		return Environment.GetEnvironmentVariable("PLAINTORCH_VAULT_PATH")
			?? configurationStore.Load().ActiveVaultPath
			?? throw new InvalidOperationException("No active PLAINTORCH vault is configured. Run 'activate' in a PLAINTORCH vault first.");
	}
}