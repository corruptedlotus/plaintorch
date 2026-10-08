using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// Makes the compensations a decision goes ahead with (PEP103): each item moves to the status the decision names for it, the
/// move goes on the record, and the item's note is rewritten once the save commits. It runs inside the save the requested
/// change rides, so the change and its compensations commit together or not at all.
/// </summary>
public sealed class StateCompensator(EntityLifecycleResolver lifecycleResolver, IServiceProvider serviceProvider)
{
	/// <summary>
	/// Applies the compensations of a decision that goes ahead with them.
	/// </summary>
	/// <exception cref="InvalidOperationException">A compensation names an item the judge did not resolve.</exception>
	public void Apply(StatePass pass, StateJudgement judgement, StateDecision decision)
	{
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(judgement);
		ArgumentNullException.ThrowIfNull(decision);
		if (decision.Outcome != StateDecisionOutcome.Compensate || decision.Compensations.Count == 0)
		{
			return;
		}

		var auditLog = serviceProvider.GetRequiredService<VaultAuditLogService>();
		foreach (var compensation in decision.Compensations)
		{
			var entity = judgement.EntityOf(compensation.Subject)
				?? throw new InvalidOperationException($"The compensation for {StateJudge.Name(compensation.Subject)} names an item the save does not hold.");
			var statusType = lifecycleResolver.StatusPropertyOf(entity.GetType())?.PropertyType
				?? throw new InvalidOperationException($"{StateJudge.Name(compensation.Subject)} has no status a compensation could set.");
			var status = (Enum)Enum.Parse(statusType, compensation.Status!);
			lifecycleResolver.SetStatus(entity, status);
			auditLog.Stage(
				"state",
				"state.compensate",
				subject: entity,
				details: new
				{
					from = compensation.From.ToString(),
					to = compensation.To.ToString(),
					status = compensation.Status,
					reason = compensation.Reason,
					level = decision.Level.ToString(),
					rules = decision.Conflicts.Select(conflict => conflict.Rule).Distinct().ToList(),
				});
			pass.Move(entity);
		}
	}
}
