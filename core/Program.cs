global using A11d.Module;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pleiades.Calendar;
using Pleiades.Plaintorch.Api.Contracts;
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
		if (command is not ("init" or "activate" or "deactivate" or "serve" or "bootstrap-service"))
		{
			Console.Error.WriteLine("Unknown command. Supported commands: init, activate, deactivate, serve, bootstrap-service.");
			return 1;
		}

		var userLayout = ResolveUserLayout(command, args);
		var configurationStore = new PlaintorchUserConfigurationStore(userLayout);
		var vaultPath = TryResolveVaultPath(command, args);
		if (command == "serve")
		{
			ApplyServeVaultOverride(args);
		}

		var builder = WebApplication.CreateBuilder(args);
		builder.Host.UseWindowsService();
		builder.Host.UseSystemd();
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
			// Tri-state update fields: a present key (value or explicit null) applies; an omitted key leaves unchanged.
			options.SerializerOptions.Converters.Add(new OptionalJsonConverterFactory());
			options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
			{
				Modifiers =
				{
					AddRuntimeTypeNameProperty,
				},
			};
		});
		builder.Services.AddCors(options =>
		{
			options.AddPolicy("PlaintorchGlobalCors", policy =>
				policy
					.AllowAnyOrigin()
					.AllowAnyHeader()
					.AllowAnyMethod());
		});
		if (vaultPath is not null)
		{
			builder.Services.AddSingleton(new VaultOptions
			{
				VaultPath = vaultPath,
			});
		}
		builder.Services.AddSingleton(userLayout);
		if (OperatingSystem.IsWindows())
		{
			// A Node client on Windows resolves a socket path to a named pipe, not an AF_UNIX socket, so register the
			// named-pipe transport alongside the default socket transport (which still serves the opt-in loopback endpoint).
			builder.WebHost.UseNamedPipes();
		}

		builder.WebHost.ConfigureKestrel(options =>
		{
			userLayout.EnsureExists();
			if (userLayout.LoopbackEnabled)
			{
				options.ListenLocalhost(userLayout.LoopbackPort, listenOptions =>
				{
					listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
				});
			}

			if (OperatingSystem.IsWindows())
			{
				// Windows clients reach the core over this named pipe; a .NET AF_UNIX socket is unreachable from Node there.
				options.ListenNamedPipe(userLayout.PipeName, listenOptions =>
				{
					listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
				});
			}
			else
			{
				TryDeleteStaleSocket(userLayout.SocketPath);
				options.ListenUnixSocket(userLayout.SocketPath, listenOptions =>
				{
					listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
				});
			}
		});

		var app = builder.Install<PLAINTORCH>().Build();
		app.UseRouting();
		app.UseCors("PlaintorchGlobalCors");
		app.Configure<PLAINTORCH>();

		if (command == "serve")
		{
			Console.WriteLine($"PLAINTORCH endpoint: {userLayout.EndpointDisplay}");
			if (userLayout.LoopbackEnabled)
			{
				Console.WriteLine($"PLAINTORCH loopback: {userLayout.LoopbackBaseUrl}");
			}

			if (userLayout.IsEphemeral)
			{
				Console.WriteLine("PLAINTORCH environment: ephemeral (dev)");
			}
			else if (userLayout.IsDevProfile)
			{
				Console.WriteLine("PLAINTORCH environment: dev sub-profile (persistent; pass --daemon to run the real per-user profile)");
			}
		}

		switch (command)
		{
			case "init":
				return await RunInitializeAsync(app, vaultPath!);

			case "activate":
				return RunActivate(app, vaultPath!);

			case "deactivate":
				return RunDeactivate(app);

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
	/// Runs the long-lived PLAINTORCH host for Windows service or systemd execution.
	/// </summary>
	/// <remarks>
	/// The host starts idle without a vault. Vault activation, lock ownership, and vault initialization are
	/// coordinated at runtime by <see cref="PlaintorchCoreService"/> in response to user settings, so that a
	/// vault can be activated and deactivated without restarting the host.
	/// </remarks>
	private static async Task<int> RunServeAsync(WebApplication application, bool daemon = false)
	{
		var splashService = application.Services.GetRequiredService<PlaintorchCoreSplashService>();

		try
		{
			await application.RunAsync();
			return 0;
		}
		catch (Exception exception)
		{
			await splashService.ShowErrorAsync("PLAINTORCH core failed to start.", exception);
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
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

	private static void AddRuntimeTypeNameProperty(JsonTypeInfo jsonTypeInfo)
	{
		if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
		{
			return;
		}

		if (jsonTypeInfo.Properties.Any(property => string.Equals(property.Name, "@type", StringComparison.Ordinal)))
		{
			return;
		}

		var runtimeTypeProperty = jsonTypeInfo.CreateJsonPropertyInfo(typeof(string), "@type");
		runtimeTypeProperty.Get = value => value?.GetType().Name;
		runtimeTypeProperty.Set = null;
		runtimeTypeProperty.Order = int.MinValue;
		jsonTypeInfo.Properties.Add(runtimeTypeProperty);
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
	/// Determines whether a boolean command-line flag is present.
	/// </summary>
	private static bool HasArgumentFlag(IReadOnlyList<string> args, string flag)
	{
		foreach (var argument in args)
		{
			if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines whether the process is running under the installed service runner (Windows Service or systemd).
	/// </summary>
	private static bool IsRunningAsService()
	{
		return WindowsServiceHelpers.IsWindowsService() || SystemdHelpers.IsSystemdService();
	}

	/// <summary>
	/// Resolves the per-user host layout for the current command and flags.
	/// </summary>
	private static PlaintorchUserLayout ResolveUserLayout(string command, IReadOnlyList<string> args)
	{
		var layout = ResolveUserLayoutRoot(command, args);
		layout.LoopbackEnabled = HasArgumentFlag(args, "--loopback");
		return layout;
	}

	/// <summary>
	/// Selects which per-user host profile a command should run against.
	/// </summary>
	/// <remarks>
	/// Only <c>serve</c> has development profiles. <c>serve --ephemeral</c> gets a throwaway temp profile; an ordinary
	/// manual <c>serve</c> gets the current user's persistent development sub-profile (<c>~/.pleiades/plaintorch-dev</c>)
	/// so a sandbox never collides with a real installed daemon and no separate OS account is required. The installed
	/// service runner, and any manual <c>serve --daemon</c> for a developer building their own background daemon, use the
	/// real per-user profile. Every non-<c>serve</c> command also uses the real per-user profile.
	/// </remarks>
	private static PlaintorchUserLayout ResolveUserLayoutRoot(string command, IReadOnlyList<string> args)
	{
		if (command != "serve")
		{
			return PlaintorchUserLayout.CreateDefault();
		}

		if (HasArgumentFlag(args, "--ephemeral"))
		{
			return PlaintorchUserLayout.CreateEphemeral();
		}

		if (!IsRunningAsService() && !HasArgumentFlag(args, "--daemon"))
		{
			return PlaintorchUserLayout.CreateDevProfile();
		}

		return PlaintorchUserLayout.CreateDefault();
	}

	/// <summary>
	/// Resolves the vault path from command-line arguments or environment variables.
	/// </summary>
	private static string? TryResolveVaultPath(string command, IReadOnlyList<string> args)
	{
		if (command is "serve")
		{
			// serve starts idle and activates its vault from user settings at runtime, so it registers no VaultOptions.
			// An explicit `serve --vault` is instead applied as a runtime override (see ApplyServeVaultOverride).
			return null;
		}

		return ExtractVaultArgument(args) ?? Directory.GetCurrentDirectory();
	}

	/// <summary>
	/// Reads the value of a <c>--vault &lt;path&gt;</c> argument, or <see langword="null"/> when it is absent.
	/// </summary>
	private static string? ExtractVaultArgument(IReadOnlyList<string> args)
	{
		for (var index = 0; index < args.Count - 1; index++)
		{
			if (string.Equals(args[index], "--vault", StringComparison.OrdinalIgnoreCase))
			{
				return args[index + 1];
			}
		}

		return null;
	}

	/// <summary>
	/// Applies an explicit <c>serve --vault &lt;path&gt;</c> as a direct runtime vault target. A manual/dev serve is
	/// meant to serve a specific vault without editing user configuration, so the argument is surfaced through the same
	/// <c>PLAINTORCH_VAULT_PATH</c> channel that <see cref="PlaintorchCoreService"/> already prefers over the persisted
	/// active-vault setting — the config is ignored, exactly as a sandbox serve expects.
	/// </summary>
	private static void ApplyServeVaultOverride(IReadOnlyList<string> args)
	{
		var vaultArgument = ExtractVaultArgument(args);
		if (string.IsNullOrWhiteSpace(vaultArgument))
		{
			return;
		}

		Environment.SetEnvironmentVariable("PLAINTORCH_VAULT_PATH", Path.GetFullPath(vaultArgument));
	}
}