using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// The state enforcement engine (PEP103): one pass over a save, run by <see cref="StateInterceptor"/> before anything is
/// written, in four phases.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// <b>Structure</b> (<see cref="StructureReactions"/>, then <see cref="MembershipReactions"/>): what the requested change
/// implies whatever the rules say — releases on delete, schedule cursors, occurrence hardening, purges of the edges the
/// structure contradicts — and then the moves the engine makes of its own accord when an objective joins the active onrush
/// sprint or Polaris cycle, wherever nothing blocks them.
/// </description></item>
/// <item><description>
/// <b>Judgement</b> (<see cref="StateJudge"/>, <see cref="StateDecisionService"/>, <see cref="StateCompensator"/>): the rules
/// the save would break become conflicts, and the backlog state enforcement level decides. A save that waits for
/// confirmation or is refused stops here with <see cref="StateEnforcementException"/>; one that goes ahead with
/// compensations has them applied.
/// </description></item>
/// <item><description>
/// <b>Outcomes</b> (<see cref="OutcomeReactions"/>): what the final state implies — next occurrences, forecast supersession,
/// settlements and rewards, dependency satisfaction and checkpoint unlocks.
/// </description></item>
/// <item><description>
/// <b>Records</b> (<see cref="BacklogScheduleRecorder"/>): the lifecycle states every item reached or left.
/// </description></item>
/// </list>
/// <para>
/// The note work a committed save leaves is returned rather than done: the items the engine moved get a reconcile intent in
/// the same transaction, unless their caller already writes their note.
/// </para>
/// <para>
/// Context-bound services (the audit log, the write queue) are resolved from the scope when needed rather than injected:
/// they depend on the <see cref="PlainfraContext"/> whose options wire the interceptor, so a constructor dependency would form
/// a resolution cycle. The scoped context already exists by the time a save runs, so they bind to the same unit of work.
/// </para>
/// </remarks>
public sealed class StateEngine(
	StructureReactions structure,
	MembershipReactions membership,
	StateJudge judge,
	StateDecisionService decisions,
	StateCompensator compensator,
	OutcomeReactions outcomes,
	BacklogScheduleRecorder recorder,
	IServiceProvider serviceProvider)
{
	/// <summary>
	/// Runs the engine over the save pending on <paramref name="context"/>.
	/// </summary>
	/// <returns>The note work to do once the save commits.</returns>
	/// <exception cref="StateEnforcementException">The save waits for confirmation or is refused.</exception>
	public async Task<StateNoteWork> RunAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		var pass = new StatePass(context);

		await structure.ApplyAsync(pass, cancellationToken);
		await membership.ApplyAsync(pass, cancellationToken);

		var judgement = await judge.JudgeAsync(context, cancellationToken);
		var decision = decisions.Decide(judgement.Conflicts);
		decision.ThrowIfStopped();
		if (decision.Outcome == StateDecisionOutcome.Compensate)
		{
			compensator.Apply(pass, judgement, decision);
		}
		else if (decision.Conflicts.Count > 0)
		{
			RecordIgnored(decision);
		}

		await outcomes.ApplyAsync(pass, cancellationToken);
		await recorder.ApplyAsync(context, cancellationToken);
		return await ConcludeAsync(pass, cancellationToken);
	}

	/// <summary>
	/// Puts the conflicts an <see cref="BacklogStateEnforcementLevel.Ignore"/> level let through on the record, so what the
	/// backlog was allowed to break can be traced.
	/// </summary>
	private void RecordIgnored(StateDecision decision)
	{
		serviceProvider.GetRequiredService<VaultAuditLogService>().Stage(
			"state",
			"state.ignore",
			details: new
			{
				level = decision.Level.ToString(),
				conflicts = decision.Conflicts.Select(conflict => new { conflict.Rule, conflict.Subject.Kind, conflict.Subject.Id, conflict.Message }).ToList(),
			});
	}

	/// <summary>
	/// Records a reconcile intent for each item the engine moved, in the same transaction, and returns the note work for
	/// after the commit. An item whose caller already recorded an intent for it is left to that caller, which drains it with
	/// what it knows of the item's previous state.
	/// </summary>
	private async Task<StateNoteWork> ConcludeAsync(StatePass pass, CancellationToken cancellationToken)
	{
		if (pass.Moved.Count == 0)
		{
			return pass.SupersededForecasts.Count == 0 ? StateNoteWork.None : new StateNoteWork(pass.SupersededForecasts, []);
		}

		var writeQueue = serviceProvider.GetRequiredService<VaultWriteQueue>();
		var reconciles = new List<object>();
		foreach (var entity in pass.Moved)
		{
			if (entity is not IPuckNamedEntity named || pass.Context.Entry(entity).State is EntityState.Deleted or EntityState.Detached)
			{
				continue;
			}

			if (HasPendingIntent(pass.Context, entity.GetType(), named.Id))
			{
				continue;
			}

			await writeQueue.RecordReconcileAsync(entity, cancellationToken);
			reconciles.Add(entity);
		}

		return new StateNoteWork(pass.SupersededForecasts, reconciles);
	}

	private static bool HasPendingIntent(PlainfraContext context, Type entityType, string id)
		=> context.ChangeTracker.Entries<VaultWriteIntent>().Any(entry =>
			entry.State is EntityState.Added or EntityState.Modified
			&& string.Equals(entry.Entity.EntityType, entityType.FullName, StringComparison.Ordinal)
			&& string.Equals(entry.Entity.EntityId, id, StringComparison.OrdinalIgnoreCase));
}
