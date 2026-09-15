using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// Streams the host's lifecycle to the parent process as JSON lines on standard output.
/// </summary>
/// <remarks>
/// The channel a desktop shell drives its splash and tray from without probing the API: events arrive in order,
/// the first one before the socket is even bound, and the process exit itself is the crash signal the API could
/// never deliver. Only registered in <see cref="PlaintorchLaunchMode.Spawn"/>, where console logging is disabled so
/// nothing else writes to stdout. Every line is one object with an <c>event</c> discriminator:
/// <list type="bullet">
/// <item><c>hello</c>: process id, profile root, and the endpoint the host is about to bind.</item>
/// <item><c>status</c>: a <see cref="PlaintorchHostStatus"/> transition.</item>
/// <item><c>listening</c>: the endpoint (and loopback URL, when enabled) is accepting connections.</item>
/// <item><c>stopping</c>: graceful shutdown began.</item>
/// </list>
/// </remarks>
public sealed class PlaintorchHostStatusStream : IHostedService, IDisposable
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly PlaintorchHostState _state;
	private readonly PlaintorchUserLayout _layout;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly object _gate = new();
	private readonly StreamWriter _output;

	/// <summary>
	/// Initializes the stream over the process's standard output.
	/// </summary>
	public PlaintorchHostStatusStream(PlaintorchHostState state, PlaintorchUserLayout layout, IHostApplicationLifetime lifetime)
	{
		_state = state;
		_layout = layout;
		_lifetime = lifetime;
		_output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		Emit(new
		{
			@event = "hello",
			pid = Environment.ProcessId,
			profile = _layout.RootPath,
			endpoint = _layout.EndpointDisplay,
			loopback = _layout.LoopbackEnabled ? _layout.LoopbackBaseUrl : null,
		});
		Emit(ToEvent(_state.Current));

		_state.Changed += OnStateChanged;
		_lifetime.ApplicationStarted.Register(() => Emit(new
		{
			@event = "listening",
			endpoint = _layout.EndpointDisplay,
			loopback = _layout.LoopbackEnabled ? _layout.LoopbackBaseUrl : null,
		}));
		_lifetime.ApplicationStopping.Register(() =>
		{
			_state.Report(PlaintorchHostPhase.Stopping, "Stopping PLAINTORCH core...");
			Emit(new { @event = "stopping" });
		});
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		_state.Changed -= OnStateChanged;
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_state.Changed -= OnStateChanged;
		_output.Dispose();
	}

	/// <summary>
	/// Writes one event straight to standard output, for a launcher that has no running stream to go through
	/// (a startup failure after the host was torn down, or a refusal to start at all).
	/// </summary>
	/// <param name="payload">The event object; it must carry an <c>event</c> discriminator.</param>
	public static void WriteStandalone(object payload)
	{
		ArgumentNullException.ThrowIfNull(payload);
		try
		{
			Console.Out.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
			Console.Out.Flush();
		}
		catch (IOException)
		{
		}
	}

	/// <summary>
	/// Shapes a host status as the <c>status</c> event.
	/// </summary>
	public static object ToEvent(PlaintorchHostStatus status)
	{
		ArgumentNullException.ThrowIfNull(status);
		return new
		{
			@event = "status",
			phase = status.Phase,
			message = status.Message,
			vault = status.VaultPath,
			at = status.At,
		};
	}

	private void OnStateChanged(PlaintorchHostStatus status)
	{
		Emit(ToEvent(status));
	}

	private void Emit(object payload)
	{
		var line = JsonSerializer.Serialize(payload, SerializerOptions);
		lock (_gate)
		{
			try
			{
				_output.WriteLine(line);
			}
			catch (IOException)
			{
				// The parent went away; the stdin watcher will stop the host, and there is nobody left to tell.
			}
			catch (ObjectDisposedException)
			{
			}
		}
	}
}
