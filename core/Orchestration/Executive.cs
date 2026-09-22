using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents an execution record captured inside a Polaris cycle.
/// </summary>
public sealed class Executive : ITimeAllocated
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
	/// Gets or sets the incentive this execution record works at — an <see cref="Objective"/> or a
	/// <see cref="Decree"/>, never an occurrence (PEP111). An objective-backed executive is the day's plan to work
	/// that objective; a decree-backed one is the cycle's execution of that decree's routine (replacing the former
	/// Polaris-bound attentive).
	/// </summary>
	public string? IncentiveId { get; set; }

	[ForeignKey(nameof(IncentiveId))]
	[InverseProperty(nameof(Incentive.Executives))]
	/// <summary>
	/// Gets or sets the incentive this execution record works at.
	/// </summary>
	public Incentive? Incentive { get; set; }

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
	/// Gets or sets the optional timeframe this executive prefers for its execution (its affinity).
	/// Affinity is purely semantic: it flags a preferred portion of the day and enforces nothing.
	/// </summary>
	public long? AffinityTimeframeId { get; set; }

	[ForeignKey(nameof(AffinityTimeframeId))]
	/// <summary>
	/// Gets or sets the preferred timeframe for this executive's execution.
	/// </summary>
	public Timeframe? AffinityTimeframe { get; set; }

	/// <summary>
	/// Reconciles the executive time allocations through the shared rules of <see cref="TimeAllocations.Normalize"/>.
	/// </summary>
	public void NormalizeTimeAllocations()
	{
		this.Normalize();
	}
}