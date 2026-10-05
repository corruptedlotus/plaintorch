using Microsoft.Extensions.Options;
using Pleiades.Plaintorch.Api;
using Pleiades.Plaintorch.Preferences;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// The one place a requested change is judged against the backlog state enforcement level (PEP103). The engine finds the
/// conflicts a change would cause and the compensations that would resolve them; this decides, from the level, the request's
/// flags and whether anyone can be asked, whether the change goes ahead, goes ahead with its compensations, waits for
/// confirmation, or is refused.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>No conflict: the change proceeds.</description></item>
/// <item><description>Any systemic conflict: refused, whatever the level.</description></item>
/// <item><description><see cref="BacklogStateEnforcementLevel.Ignore"/>: the change proceeds, lifecycle conflicts and all.</description></item>
/// <item><description><see cref="BacklogStateEnforcementLevel.Block"/>: refused.</description></item>
/// <item><description><see cref="BacklogStateEnforcementLevel.Auto"/>: proceeds with its compensations, provided every
/// conflict offers them and they agree (no item moved to two states).</description></item>
/// <item><description><see cref="BacklogStateEnforcementLevel.Warn"/>: on the same condition, held back for confirmation —
/// or compensated at once when the client forces it (<see cref="PlaintorchRequestFlags.ForceCompensation"/>). A change
/// nobody can be asked about, from a note edit or a background pass, is refused.</description></item>
/// </list>
/// A change that cannot be compensated is refused under Auto and Warn alike.
/// </remarks>
public sealed class StateDecisionService(IOptionsSnapshot<BacklogPreferences> preferences, PlaintorchRequestFlags flags)
{
	/// <summary>
	/// Gets the enforcement level in force.
	/// </summary>
	public BacklogStateEnforcementLevel Level => preferences.Value.StateEnforcement;

	/// <summary>
	/// Decides what becomes of a requested change that would cause <paramref name="conflicts"/>.
	/// </summary>
	public StateDecision Decide(IReadOnlyList<StateConflict> conflicts)
	{
		ArgumentNullException.ThrowIfNull(conflicts);
		var level = Level;
		if (conflicts.Count == 0)
		{
			return new StateDecision(StateDecisionOutcome.Proceed, level, [], [], string.Empty);
		}

		var systemic = conflicts.Where(conflict => conflict.Kind == StateConflictKind.Systemic).ToList();
		if (systemic.Count > 0)
		{
			return Refuse(level, conflicts, Describe(systemic));
		}

		switch (level)
		{
			case BacklogStateEnforcementLevel.Ignore:
				return new StateDecision(StateDecisionOutcome.Proceed, level, conflicts, [], string.Empty);
			case BacklogStateEnforcementLevel.Block:
				return Refuse(level, conflicts, Describe(conflicts));
		}

		var stuck = conflicts.Where(conflict => !conflict.IsCompensable).ToList();
		if (stuck.Count > 0)
		{
			return Refuse(level, conflicts, $"{Describe(stuck)} Nothing the core could move would resolve it.");
		}

		if (!TryMerge(conflicts, out var compensations, out var clash))
		{
			return Refuse(level, conflicts, clash);
		}

		if (level == BacklogStateEnforcementLevel.Auto || (flags.Interactive && flags.ForceCompensation))
		{
			return new StateDecision(StateDecisionOutcome.Compensate, level, conflicts, compensations, string.Empty);
		}

		return flags.Interactive
			? new StateDecision(StateDecisionOutcome.Confirm, level, conflicts, compensations,
				$"{Describe(conflicts)} Going ahead would also {DescribeMoves(compensations)}. Confirm with the {PlaintorchRequestFlags.EnforcementHeader}: {PlaintorchRequestFlags.ForceCompensationValue} header, or cancel.")
			: Refuse(level, conflicts, $"{Describe(conflicts)} State enforcement is set to warn, and there is nobody to confirm this change.");
	}

	private static StateDecision Refuse(BacklogStateEnforcementLevel level, IReadOnlyList<StateConflict> conflicts, string message)
		=> new(StateDecisionOutcome.Refuse, level, conflicts, [], message);

	/// <summary>
	/// Gathers the compensations of every conflict, each item moved once. Two conflicts asking the same move share it; two
	/// asking different states of one item cannot both be met.
	/// </summary>
	private static bool TryMerge(IReadOnlyList<StateConflict> conflicts, out IReadOnlyList<StateCompensation> merged, out string clash)
	{
		var moves = new List<StateCompensation>();
		foreach (var compensation in conflicts.SelectMany(conflict => conflict.Compensations))
		{
			var same = moves.FindIndex(move => move.Subject.Kind == compensation.Subject.Kind
				&& string.Equals(move.Subject.Id, compensation.Subject.Id, StringComparison.OrdinalIgnoreCase));
			if (same < 0)
			{
				moves.Add(compensation);
				continue;
			}

			if (moves[same].To != compensation.To)
			{
				merged = [];
				clash = $"Resolving this change would need {Name(compensation.Subject)} to be both {moves[same].To} and {compensation.To}.";
				return false;
			}
		}

		merged = moves;
		clash = string.Empty;
		return true;
	}

	private static string Describe(IEnumerable<StateConflict> conflicts)
		=> string.Join(" ", conflicts.Select(conflict => conflict.Message.TrimEnd('.') + "."));

	private static string DescribeMoves(IEnumerable<StateCompensation> compensations)
		=> string.Join("; ", compensations.Select(move => $"move {Name(move.Subject)} to {move.Status ?? move.To.ToString()}"));

	private static string Name(StateSubject subject)
		=> subject.Title is { Length: > 0 } title ? $"{subject.Kind} '{title}' ({subject.Id})" : $"{subject.Kind} '{subject.Id}'";
}
