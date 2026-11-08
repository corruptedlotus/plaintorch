using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the backlog-schedule application API (PEP101). Adding a schedule only inserts it: replacing the slot's
/// previous schedule, like writing records, is a rule of the state engine (<see cref="Enforcement.BacklogScheduleRecorder"/>), so it holds
/// for every write pathway rather than for this service alone.
/// </summary>
public sealed class BacklogScheduleApiService(
	PlainfraContext context,
	EntityLifecycleResolver lifecycleResolver,
	VaultAuditLogService auditLogService) : IBacklogScheduleApi
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<BacklogSchedule>> ListAsync(string? entityId = null, CancellationToken cancellationToken = default)
	{
		var query = context.BacklogSchedules.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(entityId))
		{
			query = query.Where(schedule => schedule.IncentiveId == entityId
				|| schedule.DirectiveId == entityId
				|| schedule.CheckpointId == entityId);
		}

		return await query
			.OrderBy(schedule => schedule.Time)
			.ThenBy(schedule => schedule.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<BacklogSchedule> AddAsync(string entityId, BacklogScheduleType type, LifecycleState state, DateTime time, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		if (!Enum.IsDefined(type))
		{
			throw new InvalidOperationException($"'{type}' is not a backlog schedule type.");
		}

		// A plan places the start or the finish an item is meant to reach (PEP103). Every item is open until it starts, and
		// setting one aside is not planned.
		if (state is not (LifecycleState.Started or LifecycleState.Finished))
		{
			throw new InvalidOperationException($"'{state}' is not a state a plan places: a plan places a start or a finish.");
		}

		if (type == BacklogScheduleType.Record)
		{
			throw new InvalidOperationException("A record states how things turned out, so only the core writes one, as the item's lifecycle status changes. Add a plan instead.");
		}

		var owner = await ResolveOwnerAsync(entityId, cancellationToken);
		// Only a state the owner's kind passes through can be planned: a checkpoint never starts, and a decree never finishes.
		// A fate's finish (its pass, PEP103 I-191) and a lunar directive's (going stale) can be planned.
		if (!lifecycleResolver.StatesOf(owner.GetType()).Contains(state))
		{
			var milestone = state == LifecycleState.Started ? "start" : "finish";
			throw new InvalidOperationException($"A {owner.GetType().Name} never reaches {state}, so '{entityId}' has no {milestone} to plan.");
		}

		var schedule = BacklogSchedule.For(owner, type, state, ToCivil(time));
		context.BacklogSchedules.Add(schedule);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"schedule.add",
			subjectType: nameof(BacklogSchedule),
			subjectId: schedule.Id.ToString(CultureInfo.InvariantCulture),
			details: new { owner = schedule.OwnerId, type = type.ToString(), state = state.ToString(), time = schedule.Time },
			cancellationToken: cancellationToken);
		return schedule;
	}

	/// <inheritdoc />
	public async Task DeleteAsync(long scheduleId, CancellationToken cancellationToken = default)
	{
		var schedule = await context.BacklogSchedules.FirstOrDefaultAsync(item => item.Id == scheduleId, cancellationToken)
			?? throw new InvalidOperationException($"Backlog schedule '{scheduleId}' was not found.");
		if (schedule.Type == BacklogScheduleType.Record)
		{
			throw new InvalidOperationException($"Backlog schedule '{scheduleId}' is a record of how things turned out; it goes when its owner leaves that state, or with its owner.");
		}

		context.BacklogSchedules.Remove(schedule);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"schedule.delete",
			subjectType: nameof(BacklogSchedule),
			subjectId: scheduleId.ToString(CultureInfo.InvariantCulture),
			details: new { owner = schedule.OwnerId, type = schedule.Type.ToString(), state = schedule.State.ToString(), time = schedule.Time },
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Finds the incentive, directive or checkpoint an id names. Read untracked, so the schedule added for it is not
	/// fixed up to the owner's graph and serves as the bare row.
	/// </summary>
	private async Task<object> ResolveOwnerAsync(string entityId, CancellationToken cancellationToken)
	{
		var candidates = new List<object?>
		{
			await context.Incentives.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken),
			await context.Directives.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken),
			await context.Checkpoints.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken),
		};
		var owners = candidates.OfType<object>().ToList();
		return owners.Count switch
		{
			1 => owners[0],
			0 => throw new InvalidOperationException($"No incentive, directive or checkpoint '{entityId}' was found."),
			_ => throw new InvalidOperationException($"The id '{entityId}' names more than one kind of entity, so the schedule's owner is ambiguous."),
		};
	}

	/// <summary>
	/// Reads a requested time as a civil wall-clock moment. A time sent with a UTC marker or an offset arrives as an
	/// instant; storing it as it came would read back as the UTC or foreign wall clock, so it is converted to the local
	/// wall clock first. A time sent without either is already civil.
	/// </summary>
	private static DateTime ToCivil(DateTime time) => time.Kind switch
	{
		DateTimeKind.Utc => DateTime.SpecifyKind(time.ToLocalTime(), DateTimeKind.Unspecified),
		DateTimeKind.Local => DateTime.SpecifyKind(time, DateTimeKind.Unspecified),
		_ => time,
	};
}
