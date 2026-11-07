using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// One objective joining the active onrush sprint or Polaris cycle in a save (PEP103 patch 3).
/// </summary>
/// <param name="Objective">The objective, as the save tracks it.</param>
/// <param name="Membership">What it joins, as a message words it: <c>onrush sprint</c> or <c>Polaris cycle</c>.</param>
internal sealed record MembershipJoin(Objective Objective, string Membership);

/// <summary>
/// The moves the engine makes of its own accord when an objective joins (PEP103, Patch103.3): coming on board the active
/// onrush sprint makes it <see cref="ObjectiveStatus.Onboard"/>, and joining the active Polaris cycle makes it
/// <see cref="ObjectiveStatus.Polaris"/>, each where nothing blocks the move. They run last in the structure phase, on the
/// memberships the save leaves, so the judge sees the statuses they set.
/// </summary>
/// <remarks>
/// <para>
/// <b>Joining.</b> An objective joins the active sprint when the save puts it in a sprint that is active as the save leaves
/// it (created there or moved there), or when the save begins its sprint: an active sprint it creates (the placeholder's
/// begin among them), or a sprint it gives a start that had none. It joins the active Polaris cycle the same way through an
/// executive: one created in the active cycle, moved into it or pointed at the objective, or every executive of a cycle the
/// save begins. A sprint or cycle taken up again after it ended does not begin, so its finished members are not reopened.
/// Joining the planning placeholder, a forecast, or a sprint or cycle that has ended moves nothing.
/// </para>
/// <para>
/// <b>What moves.</b> Joining pushes an objective forward only (user ruling, PEP103 patch 5): one that is open short of the
/// target (Standby or Blocked, and Onboard on its way to Polaris) moves to it; one already started, finished or set aside
/// keeps its status, and the engine never overrides a status the save sets itself. When both apply, the sprint's move comes
/// first and the cycle's then takes the objective further. A closed objective (Archived) joining is the judge's
/// (<see cref="StateJudge.ClosedJoinRule"/>): it steps back to Done, or the join waits or is refused, as the level decides.
/// </para>
/// <para>
/// <b>Only where nothing blocks it.</b> A move is made only where the lifecycle rules allow it as the save stands
/// (<see cref="StateJudge.AllowsAsync"/>): a held gate leaves the status as it is at every level, and the join itself goes
/// ahead. The engine never compensates for its own moves nor asks about them. Each move is put on the record
/// (<c>state.join</c>) and the objective's note follows once the save commits.
/// </para>
/// <para>
/// <b>Leaving moves nothing.</b> Taking an objective out of a sprint or a cycle, or ending or deleting either, keeps every
/// status.
/// </para>
/// </remarks>
public sealed class MembershipReactions(StateJudge judge, EntityLifecycleResolver lifecycleResolver, IServiceProvider serviceProvider)
{
	/// <summary>
	/// Applies the membership moves to the save.
	/// </summary>
	public async Task ApplyAsync(StatePass pass, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pass);
		var context = pass.Context;
		// A status the save sets itself is read before the engine sets any, so the sprint's move never hides it from the
		// cycle's.
		var asked = context.ChangeTracker.Entries<Objective>()
			.Where(entry => entry.State == EntityState.Modified && entry.Property(nameof(Objective.Status)).IsModified)
			.Select(entry => entry.Entity)
			.ToHashSet(ReferenceEqualityComparer.Instance);

		var boarding = await BoardingAsync(context, cancellationToken);
		await MoveAsync(pass, boarding.Where(objective => !asked.Contains(objective)).ToList(), ObjectiveStatus.Onboard, "onrush", cancellationToken);

		var joining = await JoiningPolarisAsync(context, cancellationToken);
		await MoveAsync(pass, joining.Where(objective => !asked.Contains(objective)).ToList(), ObjectiveStatus.Polaris, "polaris", cancellationToken);
	}

	/// <summary>
	/// Every objective joining the active sprint or the active Polaris cycle in this save, as the save stands, with what it
	/// joins. The judge reads it for closed objectives (<see cref="StateJudge.ClosedJoinRule"/>).
	/// </summary>
	internal static async Task<List<MembershipJoin>> JoinsAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var joins = (await BoardingAsync(context, cancellationToken)).Select(objective => new MembershipJoin(objective, "onrush sprint")).ToList();
		joins.AddRange((await JoiningPolarisAsync(context, cancellationToken)).Select(objective => new MembershipJoin(objective, "Polaris cycle")));
		return joins;
	}

	/// <summary>
	/// The objectives coming on board the active sprint in this save.
	/// </summary>
	private static async Task<List<Objective>> BoardingAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var boarding = new List<Objective>();
		foreach (var entry in context.ChangeTracker.Entries<Objective>().ToList())
		{
			if (entry.Entity.OnrushSprintId is { } sprintId
				&& await ChangedAsync(entry, nameof(Objective.OnrushSprintId), cancellationToken)
				&& await IsActiveSprintAsync(context, sprintId, cancellationToken))
			{
				boarding.Add(entry.Entity);
			}
		}

		foreach (var entry in context.ChangeTracker.Entries<OnrushSprint>().ToList())
		{
			if (StateJudge.IsActiveOnrush(entry.Entity)
				&& await BeginsAsync(entry, nameof(OnrushSprint.StartDate), cancellationToken))
			{
				boarding.AddRange(await MembersAsync(context, entry.Entity.Id, cancellationToken));
			}
		}

		return boarding.Distinct(ReferenceEqualityComparer.Instance).Cast<Objective>().ToList();
	}

	/// <summary>
	/// The objectives joining the active Polaris cycle in this save, through their executives.
	/// </summary>
	private static async Task<List<Objective>> JoiningPolarisAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var joining = new List<Objective>();
		foreach (var entry in context.ChangeTracker.Entries<Executive>().ToList())
		{
			if (entry.Entity.IncentiveId is { } incentiveId
				&& (await ChangedAsync(entry, nameof(Executive.IncentiveId), cancellationToken)
					|| await ChangedAsync(entry, nameof(Executive.PolarisCycleId), cancellationToken))
				&& await IsActiveCycleAsync(context, entry.Entity.PolarisCycleId, cancellationToken)
				&& await ObjectiveAsync(context, incentiveId, cancellationToken) is { } objective)
			{
				joining.Add(objective);
			}
		}

		foreach (var entry in context.ChangeTracker.Entries<PolarisCycle>().ToList())
		{
			if (!StateJudge.IsActivePolaris(entry.Entity)
				|| !await BeginsAsync(entry, nameof(PolarisCycle.StartTime), cancellationToken))
			{
				continue;
			}

			foreach (var incentiveId in await ExecutiveIncentivesAsync(context, entry.Entity.Id, cancellationToken))
			{
				if (await ObjectiveAsync(context, incentiveId, cancellationToken) is { } objective)
				{
					joining.Add(objective);
				}
			}
		}

		return joining.Distinct(ReferenceEqualityComparer.Instance).Cast<Objective>().ToList();
	}

	/// <summary>
	/// Moves each objective that should move to <paramref name="target"/> and that nothing blocks. A move can unblock another
	/// (one objective's start satisfying the next one's gate), so the blocked are tried again until a round moves nothing.
	/// </summary>
	private async Task MoveAsync(StatePass pass, List<Objective> objectives, ObjectiveStatus target, string membership, CancellationToken cancellationToken)
	{
		var pending = objectives.Where(objective => Moves(objective, target)).ToList();
		var moved = true;
		while (moved && pending.Count > 0)
		{
			moved = false;
			foreach (var objective in pending.ToList())
			{
				if (!await judge.AllowsAsync(pass.Context, objective, target, cancellationToken))
				{
					continue;
				}

				var from = objective.Status;
				objective.Status = target;
				serviceProvider.GetRequiredService<VaultAuditLogService>().Stage(
					"state",
					"state.join",
					subject: objective,
					details: new { membership, from = from.ToString(), status = target.ToString() });
				pass.Move(objective);
				pending.Remove(objective);
				moved = true;
			}
		}
	}

	/// <summary>
	/// Whether joining moves an objective to <paramref name="target"/>: it is open short of the target. Joining pushes forward
	/// only (PEP103 patch 5), so nothing started, finished or set aside moves.
	/// </summary>
	private bool Moves(Objective objective, ObjectiveStatus target)
		=> objective.Status != target && lifecycleResolver.StateOf(objective.Status) == LifecycleState.Open;

	/// <summary>
	/// Whether the save sets a reference: an item it creates holds it from the start, and an item it modifies holds another
	/// than the one stored.
	/// </summary>
	private static async Task<bool> ChangedAsync(EntityEntry entry, string property, CancellationToken cancellationToken)
	{
		if (entry.State == EntityState.Added)
		{
			return true;
		}

		if (entry.State != EntityState.Modified || !entry.Property(property).IsModified)
		{
			return false;
		}

		var stored = await entry.GetDatabaseValuesAsync(cancellationToken);
		return stored is null || !string.Equals(stored[property] as string, entry.Property(property).CurrentValue as string, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Whether the save begins a sprint or cycle that is active as it leaves it: one it creates, or one stored without a start.
	/// </summary>
	private static async Task<bool> BeginsAsync(EntityEntry entry, string start, CancellationToken cancellationToken)
	{
		if (entry.State == EntityState.Added)
		{
			return true;
		}

		if (entry.State != EntityState.Modified)
		{
			return false;
		}

		var stored = await entry.GetDatabaseValuesAsync(cancellationToken);
		return stored is null || stored[start] is null;
	}

	/// <summary>
	/// Whether a sprint is the active one as the save leaves it.
	/// </summary>
	private static async Task<bool> IsActiveSprintAsync(PlainfraContext context, string sprintId, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<OnrushSprint>()
			.FirstOrDefault(entry => string.Equals(entry.Entity.Id, sprintId, StringComparison.OrdinalIgnoreCase));
		if (tracked is not null)
		{
			return tracked.State is not (EntityState.Deleted or EntityState.Detached) && StateJudge.IsActiveOnrush(tracked.Entity);
		}

		return await context.OnrushSprints
			.AsNoTracking()
			.AnyAsync(sprint => sprint.Id == sprintId
				&& sprint.Id != OnrushSprint.PlanningPlaceholderId
				&& sprint.StartDate != null
				&& sprint.EndDate == null,
				cancellationToken);
	}

	/// <summary>
	/// Whether a Polaris cycle is the active one as the save leaves it.
	/// </summary>
	private static async Task<bool> IsActiveCycleAsync(PlainfraContext context, string cycleId, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<PolarisCycle>()
			.FirstOrDefault(entry => string.Equals(entry.Entity.Id, cycleId, StringComparison.OrdinalIgnoreCase));
		if (tracked is not null)
		{
			return tracked.State is not (EntityState.Deleted or EntityState.Detached) && StateJudge.IsActivePolaris(tracked.Entity);
		}

		return await context.PolarisCycles
			.AsNoTracking()
			.AnyAsync(cycle => cycle.Id == cycleId && cycle.StartTime != null && cycle.EndTime == null, cancellationToken);
	}

	/// <summary>
	/// A sprint's objectives as the save leaves them: those stored in it that the save does not move out or delete, and those
	/// the save puts in it.
	/// </summary>
	private static async Task<List<Objective>> MembersAsync(PlainfraContext context, string sprintId, CancellationToken cancellationToken)
	{
		var stored = await context.Objectives
			.Where(objective => objective.OnrushSprintId == sprintId)
			.ToListAsync(cancellationToken);
		return stored
			.Concat(context.ChangeTracker.Entries<Objective>()
				.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
				.Select(entry => entry.Entity))
			.Where(objective => context.Entry(objective).State != EntityState.Deleted
				&& string.Equals(objective.OnrushSprintId, sprintId, StringComparison.OrdinalIgnoreCase))
			.Distinct()
			.ToList();
	}

	/// <summary>
	/// The incentives a cycle's executives work at as the save leaves them, in the same reading as <see cref="MembersAsync"/>.
	/// </summary>
	private static async Task<List<string>> ExecutiveIncentivesAsync(PlainfraContext context, string cycleId, CancellationToken cancellationToken)
	{
		var stored = await context.Set<Executive>()
			.Where(executive => executive.PolarisCycleId == cycleId && executive.IncentiveId != null)
			.ToListAsync(cancellationToken);
		return stored
			.Concat(context.ChangeTracker.Entries<Executive>()
				.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
				.Select(entry => entry.Entity))
			.Where(executive => context.Entry(executive).State != EntityState.Deleted
				&& string.Equals(executive.PolarisCycleId, cycleId, StringComparison.OrdinalIgnoreCase)
				&& executive.IncentiveId is not null)
			.Select(executive => executive.IncentiveId!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// The tracked objective an executive works at, loading it when the save does not hold it; <see langword="null"/> for a
	/// decree, or an objective the save deletes.
	/// </summary>
	private static async Task<Objective?> ObjectiveAsync(PlainfraContext context, string incentiveId, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<Objective>()
			.FirstOrDefault(entry => string.Equals(entry.Entity.Id, incentiveId, StringComparison.OrdinalIgnoreCase));
		if (tracked is not null)
		{
			return tracked.State is EntityState.Deleted or EntityState.Detached ? null : tracked.Entity;
		}

		return await context.Objectives.FirstOrDefaultAsync(objective => objective.Id == incentiveId, cancellationToken);
	}
}
