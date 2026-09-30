using Pleiades.Orchestration;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Represents the side effects produced by centralized PLAINTORCH state-policy enforcement during a save operation.
/// </summary>
/// <param name="SupersededForecasts">The forecast cycles the save superseded, whose files are deleted after it commits.</param>
/// <param name="ReopenedDirectives">The directives the save bumped from <see cref="DirectiveStatus.Over"/> back to
/// <see cref="DirectiveStatus.Fulfilled"/> (PEP102 patch 9), whose notes are reconciled after it commits.</param>
public sealed record PlaintorchStatePolicyResult(
	IReadOnlyList<PolarisCycle> SupersededForecasts,
	IReadOnlyList<StellarDirective> ReopenedDirectives)
{
	/// <summary>
	/// Gets an empty policy result.
	/// </summary>
	public static PlaintorchStatePolicyResult Empty { get; } = new(Array.Empty<PolarisCycle>(), Array.Empty<StellarDirective>());

	/// <summary>
	/// Gets a value indicating whether the save produced any side effect to apply after it commits.
	/// </summary>
	public bool IsEmpty => SupersededForecasts.Count == 0 && ReopenedDirectives.Count == 0;
}
