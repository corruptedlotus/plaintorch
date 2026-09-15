using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// The single composition root for a PLAINTORCH host: JSON conventions, CORS, the per-user transport bindings,
/// launch-mode logging, and the A11d module graph.
/// </summary>
/// <remarks>
/// Every entry point composes the core through this factory, so the console daemon, a one-shot CLI command, a
/// desktop shell spawning the core, and the test harness all build the same graph and differ only in
/// <see cref="PlaintorchHostOptions"/>. The factory builds; the caller decides what to run.
/// </remarks>
public static class PlaintorchHostFactory
{
	/// <summary>
	/// The CORS policy name applied to every endpoint.
	/// </summary>
	public const string CorsPolicyName = "PlaintorchGlobalCors";

	/// <summary>
	/// Composes and builds a host for the given options. The returned application is configured but not started.
	/// </summary>
	/// <param name="options">The launch options.</param>
	/// <returns>The built application.</returns>
	public static WebApplication Create(PlaintorchHostOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var userLayout = options.UserLayout;
		userLayout.EnsureExists();

		var builder = WebApplication.CreateBuilder(options.Arguments);
		builder.Host.UseSystemd();
		ConfigureLogging(builder, options);
		ConfigureJson(builder.Services);
		builder.Services.AddCors(cors =>
		{
			cors.AddPolicy(CorsPolicyName, policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
		});

		if (options.ResolveVaultOptions() is { } vaultOptions)
		{
			builder.Services.AddSingleton(vaultOptions);
		}

		builder.Services.AddSingleton(userLayout);
		if (options.LaunchMode == PlaintorchLaunchMode.Spawn)
		{
			builder.Services.AddHostedService<PlaintorchHostStatusStream>();
			builder.Services.AddHostedService<PlaintorchStdinShutdownService>();
		}

		ConfigureTransport(builder, userLayout);

		var app = builder.Install<PLAINTORCH>().Build();
		app.UseRouting();
		app.UseCors(CorsPolicyName);
		app.Configure<PLAINTORCH>();
		return app;
	}

	/// <summary>
	/// Picks the log sinks for the launch mode: console for interactive runs, console plus files for a daemon, and
	/// files only for a spawned child whose stdout is the status stream.
	/// </summary>
	private static void ConfigureLogging(WebApplicationBuilder builder, PlaintorchHostOptions options)
	{
		switch (options.LaunchMode)
		{
			case PlaintorchLaunchMode.Daemon:
				builder.Logging.AddProvider(new PlaintorchFileLoggerProvider(options.UserLayout.LogsRootPath));
				break;

			case PlaintorchLaunchMode.Spawn:
				builder.Logging.ClearProviders();
				builder.Logging.AddProvider(new PlaintorchFileLoggerProvider(options.UserLayout.LogsRootPath));
				break;
		}
	}

	private static void ConfigureJson(IServiceCollection services)
	{
		services.ConfigureHttpJsonOptions(json =>
		{
			json.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
			// Tri-state update fields: a present key (value or explicit null) applies; an omitted key leaves unchanged.
			json.SerializerOptions.Converters.Add(new OptionalJsonConverterFactory());
			json.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
			{
				Modifiers =
				{
					AddRuntimeTypeNameProperty,
				},
			};
		});
	}

	/// <summary>
	/// Binds the per-user transport: a named pipe on Windows (a Node client resolves a socket path to a pipe there
	/// and cannot reach a .NET AF_UNIX socket), an AF_UNIX socket elsewhere, and the opt-in loopback HTTP endpoint.
	/// </summary>
	private static void ConfigureTransport(WebApplicationBuilder builder, PlaintorchUserLayout userLayout)
	{
		if (OperatingSystem.IsWindows())
		{
			builder.WebHost.UseNamedPipes();
		}

		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			if (userLayout.LoopbackEnabled)
			{
				kestrel.ListenLocalhost(userLayout.LoopbackPort, listen => listen.Protocols = HttpProtocols.Http1);
			}

			if (OperatingSystem.IsWindows())
			{
				kestrel.ListenNamedPipe(userLayout.PipeName, listen => listen.Protocols = HttpProtocols.Http1);
			}
			else
			{
				TryDeleteStaleSocket(userLayout.SocketPath);
				kestrel.ListenUnixSocket(userLayout.SocketPath, listen => listen.Protocols = HttpProtocols.Http1);
			}
		});
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
}
