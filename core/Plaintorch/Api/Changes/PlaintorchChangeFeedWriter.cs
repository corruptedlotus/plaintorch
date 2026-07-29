using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Streams the change feed to one client as server-sent events.
/// </summary>
public static class PlaintorchChangeFeedWriter
{
	private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter() },
	};

	/// <summary>
	/// Holds the response open, writing each change as it is published until the client disconnects.
	/// </summary>
	/// <param name="context">The request being answered.</param>
	/// <param name="broker">The broker to listen to.</param>
	/// <param name="cancellationToken">Cancelled when the client goes away.</param>
	public static async Task WriteAsync(HttpContext context, PlaintorchChangeBroker broker, CancellationToken cancellationToken)
	{
		context.Response.Headers.ContentType = "text/event-stream";
		context.Response.Headers.CacheControl = "no-cache";
		context.Response.Headers["X-Accel-Buffering"] = "no";

		using var subscription = broker.Subscribe();
		var reader = subscription.Reader;

		// Sent immediately so a client can tell an established stream from one still connecting.
		await context.Response.WriteAsync(": open\n\n", cancellationToken);
		await context.Response.Body.FlushAsync(cancellationToken);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				// A heartbeat is the only thing that reveals a connection which has gone away without
				// closing cleanly, since a silent feed is otherwise indistinguishable from a dead one.
				using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				heartbeat.CancelAfter(HeartbeatInterval);

				try
				{
					while (await reader.WaitToReadAsync(heartbeat.Token))
					{
						while (reader.TryRead(out var change))
						{
							await context.Response.WriteAsync(
								$"data: {JsonSerializer.Serialize(change, SerializerOptions)}\n\n",
								cancellationToken);
						}

						await context.Response.Body.FlushAsync(cancellationToken);
					}

					return;
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					await context.Response.WriteAsync(": ping\n\n", cancellationToken);
					await context.Response.Body.FlushAsync(cancellationToken);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// The client disconnected, which is the ordinary way this ends.
		}
	}
}
