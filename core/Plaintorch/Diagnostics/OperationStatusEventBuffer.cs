using System.Collections.Concurrent;
using System.Text.Json;
using Pleiades.Diagnostics;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Diagnostics;

/// <summary>
/// The durable <see cref="IOperationStatusSink"/> for PEP108: it buffers transitions in memory (a non-blocking,
/// thread-safe enqueue safe to call from hot paths and filesystem event handlers) and drains them to the vault
/// database in batches. Draining is a separate step so persistence never blocks the reporting call; the
/// <see cref="OperationStatusPersistenceWorker"/> drives it on a timer, and tests can drive it directly.
/// </summary>
public sealed class OperationStatusEventBuffer : IOperationStatusSink
{
	private readonly ConcurrentQueue<OperationStatusTransition> _pending = new();

	/// <inheritdoc />
	public void Record(OperationStatusTransition transition)
	{
		ArgumentNullException.ThrowIfNull(transition);
		_pending.Enqueue(transition);
	}

	/// <summary>
	/// Drains all buffered transitions into the supplied context and saves them, returning the number persisted.
	/// </summary>
	public async Task<int> DrainAsync(PlainfraContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		var batch = new List<OperationStatusEvent>();
		while (_pending.TryDequeue(out var transition))
		{
			batch.Add(Map(transition));
		}

		if (batch.Count == 0)
		{
			return 0;
		}

		context.OperationStatusEvents.AddRange(batch);
		await context.SaveChangesAsync(cancellationToken);
		return batch.Count;
	}

	private static OperationStatusEvent Map(OperationStatusTransition transition) => new()
	{
		OccurredUtc = transition.OccurredUtc,
		Transition = transition.Kind,
		OperationId = transition.OperationId,
		ScopeKey = transition.ScopeKey,
		ReasonCode = transition.ReasonCode,
		Severity = transition.Severity,
		PreviousSeverity = transition.PreviousSeverity,
		FilesJson = transition.Files.Count > 0 ? JsonSerializer.Serialize(transition.Files) : null,
		EntityId = transition.EntityId,
		Detail = transition.Detail,
		OccurrenceCount = transition.OccurrenceCount,
	};
}
