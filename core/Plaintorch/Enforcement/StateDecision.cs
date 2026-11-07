namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// What becomes of a requested change once its conflicts are weighed against the enforcement level (PEP103).
/// </summary>
public enum StateDecisionOutcome
{
	/// <summary>
	/// The change goes ahead as requested: it breaks no rule, or the level ignores what it breaks.
	/// </summary>
	Proceed,

	/// <summary>
	/// The change goes ahead together with the compensations, which move the other items it needs.
	/// </summary>
	Compensate,

	/// <summary>
	/// The change waits for the user, who cancels it or repeats it forcing the compensations.
	/// </summary>
	Confirm,

	/// <summary>
	/// The change is refused.
	/// </summary>
	Refuse,
}

/// <summary>
/// The verdict on one requested change (PEP103): its outcome, the level it was judged under, the conflicts found and the
/// compensations that go with it. Produced only by <see cref="StateDecisionService"/>.
/// </summary>
/// <param name="Outcome">What becomes of the change.</param>
/// <param name="Level">The enforcement level the change was judged under.</param>
/// <param name="Conflicts">Every rule the change would break, ignored ones included.</param>
/// <param name="Compensations">
/// The moves the change needs, each item at most once: the ones made with <see cref="StateDecisionOutcome.Compensate"/>, or
/// proposed with <see cref="StateDecisionOutcome.Confirm"/>. Empty otherwise.
/// </param>
/// <param name="Message">What the change runs into, in words a prompt or a refusal can show; empty when it proceeds.</param>
public sealed record StateDecision(
	StateDecisionOutcome Outcome,
	BacklogStateEnforcementLevel Level,
	IReadOnlyList<StateConflict> Conflicts,
	IReadOnlyList<StateCompensation> Compensations,
	string Message)
{
	/// <summary>
	/// Gets whether the change goes ahead, with or without compensations.
	/// </summary>
	public bool Proceeds => Outcome is StateDecisionOutcome.Proceed or StateDecisionOutcome.Compensate;

	/// <summary>
	/// Throws a <see cref="StateEnforcementException"/> unless the change goes ahead, so a pathway stops where the decision
	/// says it must. The API answers it with 409 Conflict and the decision as its body.
	/// </summary>
	/// <exception cref="StateEnforcementException">The change waits for confirmation or is refused.</exception>
	public void ThrowIfStopped()
	{
		if (!Proceeds)
		{
			throw new StateEnforcementException(this);
		}
	}
}

/// <summary>
/// Stops a requested change that a <see cref="StateDecision"/> holds back for confirmation or refuses (PEP103). It derives
/// from <see cref="InvalidOperationException"/>, like the core's other refusals.
/// </summary>
public sealed class StateEnforcementException(StateDecision decision) : InvalidOperationException(decision.Message)
{
	/// <summary>
	/// Gets the decision that stopped the change.
	/// </summary>
	public StateDecision Decision { get; } = decision;
}
