namespace Pleiades.Orchestration;

/// <summary>
/// Represents a backlog record that carries the optional whole-minute working time allocations of PEP098.
/// </summary>
public interface ITimeAllocated
{
	/// <summary>
	/// Gets or sets the optional primary time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	int? Estimation { get; set; }

	/// <summary>
	/// Gets or sets the optional minimum time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	int? Minimum { get; set; }

	/// <summary>
	/// Gets or sets the optional maximum time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	int? Maximum { get; set; }
}

/// <summary>
/// Provides the shared reconciliation rules for working time allocations across backlog record kinds.
/// </summary>
public static class TimeAllocations
{
	/// <summary>
	/// Reconciles a record's time allocations so they honour the coupling and clamping rules:
	/// when a minimum or maximum bound is present but no estimation has been specified yet, the estimation
	/// adopts that bound (preferring the minimum); and whenever a bound is present the estimation is clamped
	/// into the resulting <c>[minimum, maximum]</c> envelope.
	/// </summary>
	public static void Normalize(this ITimeAllocated record)
	{
		ArgumentNullException.ThrowIfNull(record);
		record.Estimation ??= record.Minimum ?? record.Maximum;

		if (record.Estimation is null)
		{
			return;
		}

		if (record.Maximum is not null && record.Estimation > record.Maximum)
		{
			record.Estimation = record.Maximum;
		}

		if (record.Minimum is not null && record.Estimation < record.Minimum)
		{
			record.Estimation = record.Minimum;
		}
	}
}
