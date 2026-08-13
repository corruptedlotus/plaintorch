namespace Pleiades.Diagnostics;

/// <summary>
/// The single entry point operations use to report a run's outcome (PEP108): it ingests the report into the live
/// <see cref="OperationStatusRegistry"/> and forwards the resulting transitions to the durable sink. Operations
/// report facts (which checks they evaluated and each result); raise/resolve bookkeeping happens here, once.
/// </summary>
public sealed class OperationStatusReporter(OperationStatusRegistry registry, IOperationStatusSink sink)
{
	/// <summary>
	/// Reports the outcome of a single operation run, raising, escalating, or resolving statuses as the diff against
	/// current state dictates, and recording every resulting transition durably.
	/// </summary>
	public void Report(OperationReport report)
	{
		ArgumentNullException.ThrowIfNull(report);
		foreach (var transition in registry.Ingest(report))
		{
			sink.Record(transition);
		}
	}
}
