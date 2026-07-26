using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an execution record captured inside a Polaris cycle.
/// </summary>
public sealed class Executive
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the execution record.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the owning Polaris cycle identifier.
	/// </summary>
	public required string PolarisCycleId { get; set; }

	[ForeignKey(nameof(PolarisCycleId))]
	[InverseProperty(nameof(PolarisCycle.Executives))]
	/// <summary>
	/// Gets or sets the owning Polaris cycle.
	/// </summary>
	public PolarisCycle? PolarisCycle { get; set; }

	/// <summary>
	/// Gets or sets the related objective identifier.
	/// </summary>
	public string? ObjectiveId { get; set; }

	[ForeignKey(nameof(ObjectiveId))]
	[InverseProperty(nameof(Objective.Executives))]
	/// <summary>
	/// Gets or sets the related objective.
	/// </summary>
	public Objective? Objective { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the action was executed.
	/// </summary>
	public bool Executed { get; set; }

	/// <summary>
	/// Gets or sets the optional primary time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	/// <remarks>
	/// This is the main allocation parameter and doubles as a lightweight progress marker for the work.
	/// Within a Polaris cycle the sum of every executive estimation represents the day's total workload.
	/// </remarks>
	public int? Estimation { get; set; }

	/// <summary>
	/// Gets or sets the optional minimum time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	public int? Minimum { get; set; }

	/// <summary>
	/// Gets or sets the optional maximum time allocation, expressed as a whole-minute working time unit.
	/// </summary>
	public int? Maximum { get; set; }

	/// <summary>
	/// Gets or sets the raw count of tracked (passed) minutes spent on this executive.
	/// </summary>
	/// <remarks>
	/// Unlike the estimation/minimum/maximum allocations, this is not a target but a running tally of
	/// actually elapsed work, expressed as whole minutes. It defaults to <c>0</c> and is never clamped
	/// against the allocation envelope.
	/// </remarks>
	public int Elapsed { get; set; }

	/// <summary>
	/// Reconciles the executive time allocations so they honour the coupling and clamping rules:
	/// when a minimum or maximum bound is present but no estimation has been specified yet, the estimation
	/// adopts that bound (preferring the minimum); and whenever a bound is present the estimation is clamped
	/// into the resulting <c>[minimum, maximum]</c> envelope.
	/// </summary>
	public void NormalizeTimeAllocations()
	{
		Estimation ??= Minimum ?? Maximum;

		if (Estimation is null)
		{
			return;
		}

		if (Maximum is not null && Estimation > Maximum)
		{
			Estimation = Maximum;
		}

		if (Minimum is not null && Estimation < Minimum)
		{
			Estimation = Minimum;
		}
	}
}