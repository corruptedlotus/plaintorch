using System.IO.Pipes;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// Detects whether a PLAINTORCH core is already serving a per-user profile, by asking its health endpoint over the
/// profile's own transport.
/// </summary>
/// <remarks>
/// The per-user single-instance guard. Without it a second launch fails on the pipe or socket bind with a raw
/// exception; with it the newcomer can report the running instance and step aside. The vault lock does not cover
/// this, because an idle core holds no vault.
/// </remarks>
public static class PlaintorchInstanceProbe
{
	private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(750);

	/// <summary>
	/// Describes a core that answered the probe.
	/// </summary>
	/// <param name="Endpoint">The transport endpoint it answered on.</param>
	/// <param name="Mode">The daemon mode it reported (<c>idle</c> or <c>active</c>).</param>
	/// <param name="ActiveVault">The vault it is serving, when any.</param>
	public sealed record RunningInstance(string Endpoint, string? Mode, string? ActiveVault);

	/// <summary>
	/// Probes the profile's endpoint and returns the running instance, or <see langword="null"/> when nothing answers.
	/// </summary>
	/// <param name="layout">The per-user profile whose endpoint to probe.</param>
	/// <param name="cancellationToken">A token that cancels the probe.</param>
	public static async Task<RunningInstance?> TryDetectAsync(PlaintorchUserLayout layout, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(layout);
		if (!EndpointExists(layout))
		{
			return null;
		}

		using var handler = new SocketsHttpHandler
		{
			ConnectCallback = (_, token) => ConnectAsync(layout, token),
			ConnectTimeout = ConnectTimeout,
		};
		using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

		try
		{
			var health = await client.GetFromJsonAsync<HealthPayload>("http://plaintorch/healthz", cancellationToken);
			return health is null ? null : new RunningInstance(layout.EndpointDisplay, health.Mode, health.ActiveVault);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or SocketException or TimeoutException or System.Text.Json.JsonException)
		{
			return null;
		}
	}

	private static bool EndpointExists(PlaintorchUserLayout layout)
	{
		// A pipe that nobody serves and a socket file nobody bound both fail fast here, which keeps the common
		// "nothing is running" case instant instead of waiting out the connect timeout. Windows pipes are not
		// visible to File.Exists, but the pipe namespace enumerates like a directory.
		if (!OperatingSystem.IsWindows())
		{
			return File.Exists(layout.SocketPath);
		}

		try
		{
			return Directory.EnumerateFiles(PipeNamespace)
				.Any(entry => string.Equals(Path.GetFileName(entry), layout.PipeName, StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return true;
		}
	}

	private const string PipeNamespace = @"\\.\pipe\";

	private static async ValueTask<Stream> ConnectAsync(PlaintorchUserLayout layout, CancellationToken cancellationToken)
	{
		if (OperatingSystem.IsWindows())
		{
			var pipe = new NamedPipeClientStream(".", layout.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			try
			{
				await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken);
				return pipe;
			}
			catch
			{
				await pipe.DisposeAsync();
				throw;
			}
		}

		var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(layout.SocketPath), cancellationToken);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private sealed record HealthPayload(string? Status, string? Mode, string? ActiveVault);
}
