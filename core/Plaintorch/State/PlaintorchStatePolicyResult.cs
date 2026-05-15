using Pleiades.Orchestration;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Represents the side effects produced by centralized PLAINTORCH state-policy enforcement during a save operation.
/// </summary>
public sealed record PlaintorchStatePolicyResult(IReadOnlyList<PolarisCycle> SupersededForecasts)
{
	/// <summary>
	/// Gets an empty policy result.
	/// </summary>
	public static PlaintorchStatePolicyResult Empty { get; } = new(Array.Empty<PolarisCycle>());
}