using Microsoft.Extensions.Options;
using Pleiades.Orchestration.Lifecycle;
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
/// conflict offers them and they agree (no item both set aside and moved along its main line).</description></item>
/// <item><description><see cref="BacklogStateEnforcementLevel.Warn"/>: on the same condition, held back for confirmation —
/// or compensated at once when the client forces it (<see cref="PlaintorchRequestFlags.ForceCompensation"/>). A change
/// nobody can be asked about, from a note edit or a background pass, is refused (user ruling).</description></item>
/// </list>
/// A change that cannot be compensated is refused under Auto and Warn alike. Messages say what blocks a change, not which
/// items do ("…is blocked by 2 dependencies"); the compensations name the items confirming would move.
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
				return Refuse(level, conflicts, $"{Describe(conflicts)} State enforcement is set to block.");
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
				$"{Describe(conflicts)} Going ahead would also {DescribeMoves(compensations)}.")
			: Refuse(level, conflicts, $"{Describe(conflicts)} State enforcement is set to warn, and nobody can confirm this change here.");
	}

	/// <summary>
	/// Refuses a verb the state rules forbid outright, as a systemic conflict is refused: at every level, and answered like
	/// any refused save (409 on the API). It is for a verb whose request changes nothing the judge could read — beginning a
	/// sprint that has ended without naming a new start, say — so the verb asks here rather than relying on a save.
	/// </summary>
	public StateDecision RefuseOutright(string rule, StateSubject subject, string message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rule);
		ArgumentNullException.ThrowIfNull(subject);
		return Refuse(Level, [new StateConflict(StateConflictKind.Systemic, rule, subject, message, [])], message);
	}

	private static StateDecision Refuse(BacklogStateEnforcementLevel level, IReadOnlyList<StateConflict> conflicts, string message)
		=> new(StateDecisionOutcome.Refuse, level, conflicts, [], message);

	/// <summary>
	/// Gathers the compensations of every conflict, each item moved once. Two conflicts asking the same move share it, and
	/// two asking different states of one item's main line in the same direction take the furthest — forward, a finish
	/// enters the start on its way; back, the earlier state takes back what the later one would. Moving an item forward
	/// while another conflict moves it back, or setting it aside while another needs it on its main line, cannot be done at
	/// once: that is a clash.
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

			var kept = moves[same];
			if (kept.To == compensation.To)
			{
				continue;
			}

			var back = compensation.To < compensation.From;
			if (kept.To == LifecycleState.Deferred || compensation.To == LifecycleState.Deferred || back != kept.To < kept.From)
			{
				merged = [];
				clash = $"Resolving this change would need {Name(compensation.Subject)} to be both {kept.To} and {compensation.To}.";
				return false;
			}

			if (back ? compensation.To < kept.To : compensation.To > kept.To)
			{
				moves[same] = compensation with { From = kept.From };
			}
		}

		merged = moves;
		clash = string.Empty;
		return true;
	}

	private static string Describe(IEnumerable<StateConflict> conflicts)
		=> string.Join(" ", conflicts.Select(conflict => conflict.Message.TrimEnd('.') + "."));

	/// <summary>
	/// Sums up what confirming would move, by what happens to the items rather than which they are: "finish 2 items and
	/// start 1 item". The compensations themselves name the items.
	/// </summary>
	private static string DescribeMoves(IEnumerable<StateCompensation> compensations)
	{
		var verbs = compensations
			.GroupBy(Verb)
			.Select(group => $"{group.Key} {group.Count()} {(group.Count() == 1 ? "item" : "items")}")
			.ToList();
		return verbs.Count switch
		{
			0 => "move nothing else",
			1 => verbs[0],
			_ => $"{string.Join(", ", verbs.Take(verbs.Count - 1))} and {verbs[^1]}",
		};
	}

	private static string Verb(StateCompensation move) => move.To switch
	{
		LifecycleState.Deferred => "set aside",
		_ when move.To <= move.From => "reopen",
		LifecycleState.Started => "start",
		LifecycleState.Finished => "finish",
		_ => "reopen",
	};

	private static string Name(StateSubject subject)
		=> subject.Title is { Length: > 0 } title ? $"{subject.Kind} '{title}' ({subject.Id})" : $"{subject.Kind} '{subject.Id}'";
}
