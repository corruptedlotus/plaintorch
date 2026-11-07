using Pleiades.Plaintorch.Enforcement;

namespace Pleiades.Plaintorch.Api.Contracts;

/// <summary>
/// The body of the 409 Conflict the API answers when state enforcement stops a change (PEP103): the decision, so a client
/// can show what the change runs into and, when it waits for confirmation, which other items going ahead would move. A
/// client confirms by repeating the request with <see cref="PlaintorchRequestFlags.EnforcementHeader"/> set to
/// <see cref="PlaintorchRequestFlags.ForceCompensationValue"/>.
/// </summary>
/// <param name="Type">Always <see cref="ProblemType"/>, so a client tells this answer from other conflicts.</param>
/// <param name="Outcome"><see cref="StateDecisionOutcome.Confirm"/> or <see cref="StateDecisionOutcome.Refuse"/>.</param>
/// <param name="Level">The enforcement level the change was judged under.</param>
/// <param name="Message">What the change runs into.</param>
/// <param name="Conflicts">Every rule the change would break.</param>
/// <param name="Compensations">The moves confirming would make; empty for a refusal.</param>
public sealed record StateEnforcementProblem(
	string Type,
	StateDecisionOutcome Outcome,
	BacklogStateEnforcementLevel Level,
	string Message,
	IReadOnlyList<StateConflict> Conflicts,
	IReadOnlyList<StateCompensation> Compensations)
{
	/// <summary>
	/// The <see cref="Type"/> of every state enforcement answer.
	/// </summary>
	public const string ProblemType = "state-enforcement";

	/// <summary>
	/// Builds the answer for a decision that stopped a change.
	/// </summary>
	public static StateEnforcementProblem From(StateDecision decision)
	{
		ArgumentNullException.ThrowIfNull(decision);
		return new StateEnforcementProblem(ProblemType, decision.Outcome, decision.Level, decision.Message, decision.Conflicts, decision.Compensations);
	}
}
