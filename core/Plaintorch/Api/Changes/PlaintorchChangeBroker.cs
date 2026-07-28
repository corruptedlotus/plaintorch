using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Fans entity changes out to everything currently listening to the change feed.
/// </summary>
/// <remarks>
/// Registered as a singleton: subscribers outlive the scoped unit of work that publishes to them.
/// </remarks>
public sealed class PlaintorchChangeBroker
{
	private const int SubscriberCapacity = 256;

	private readonly ConcurrentDictionary<Guid, Channel<EntityChange>> _subscribers = new();

	/// <summary>
	/// Begins listening to the feed.
	/// </summary>
	/// <returns>A subscription that stops delivery when disposed.</returns>
	public PlaintorchChangeSubscription Subscribe()
	{
		// Dropping the oldest events keeps a subscriber that has stopped reading from growing without
		// bound. A consumer that misses events revalidates what it is showing, so a gap costs a refetch
		// rather than correctness.
		var channel = Channel.CreateBounded<EntityChange>(new BoundedChannelOptions(SubscriberCapacity)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		});

		var id = Guid.NewGuid();
		_subscribers[id] = channel;
		return new PlaintorchChangeSubscription(channel.Reader, () =>
		{
			if (_subscribers.TryRemove(id, out var removed))
			{
				removed.Writer.TryComplete();
			}
		});
	}

	/// <summary>
	/// Announces a batch of changes to every subscriber.
	/// </summary>
	/// <param name="changes">The changes to announce.</param>
	public void Publish(IReadOnlyCollection<EntityChange> changes)
	{
		if (changes.Count == 0 || _subscribers.IsEmpty)
		{
			return;
		}

		foreach (var channel in _subscribers.Values)
		{
			foreach (var change in changes)
			{
				channel.Writer.TryWrite(change);
			}
		}
	}
}

/// <summary>
/// A live subscription to the change feed.
/// </summary>
public sealed class PlaintorchChangeSubscription(ChannelReader<EntityChange> reader, Action release) : IDisposable
{
	/// <summary>
	/// Gets the reader delivering changes in the order they were published.
	/// </summary>
	public ChannelReader<EntityChange> Reader { get; } = reader;

	/// <inheritdoc />
	public void Dispose()
	{
		release();
	}
}
