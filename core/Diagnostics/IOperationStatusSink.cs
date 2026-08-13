namespace Pleiades.Diagnostics;

/// <summary>
/// Receives status transitions emitted by the registry for durable recording (PEP108). Implementations must be
/// safe to call from any thread and must not block or throw — the reporter runs on operation hot paths and on
/// filesystem event handlers.
/// </summary>
public interface IOperationStatusSink
{
	/// <summary>Records a status transition for durable persistence. Must not block or throw.</summary>
	void Record(OperationStatusTransition transition);
}

/// <summary>
/// A sink that discards transitions, letting the status core run without durable persistence (for example in
/// tests, or a subsystem that only needs the live registry).
/// </summary>
public sealed class NullOperationStatusSink : IOperationStatusSink
{
	/// <summary>Gets the shared instance.</summary>
	public static NullOperationStatusSink Instance { get; } = new();

	/// <inheritdoc />
	public void Record(OperationStatusTransition transition)
	{
	}
}
