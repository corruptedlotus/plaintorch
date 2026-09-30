using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the backlog-schedule application actions exposed by PLAINTORCH (PEP101): when an incentive, a directive or
/// a checkpoint is planned to begin or finish, and when it actually did.
/// </summary>
/// <remarks>
/// Only plans are written through this surface. Records are the core's account of how things turned out: it adds one
/// whenever an item's lifecycle begins or finishes, and withdraws it when the item's status is pulled back out of that
/// phase.
/// </remarks>
public interface IBacklogScheduleApi
{
	/// <summary>
	/// Lists backlog schedules, plans and records alike, ordered by time. When <paramref name="entityId"/> is given,
	/// only the schedules of that incentive, directive or checkpoint are listed.
	/// </summary>
	Task<IReadOnlyList<BacklogSchedule>> ListAsync(string? entityId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Adds a schedule to an incentive, a directive or a checkpoint. The owner's schedule already in the same slot — the
	/// same type and state — is replaced. <paramref name="time"/> is read as a civil wall-clock moment: one carrying an
	/// offset or a UTC marker is converted to local wall-clock time.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// No incentive, directive or checkpoint has the id; the type or state is unknown; the type is
	/// <see cref="BacklogScheduleType.Record"/>, which only the core writes; or the owner's kind never passes through the
	/// state — a checkpoint is one-shot, so it has no begin to plan.
	/// </exception>
	Task<BacklogSchedule> AddAsync(string entityId, BacklogScheduleType type, LifecyclePhase state, DateTime time, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a planned schedule.
	/// </summary>
	/// <exception cref="InvalidOperationException">The schedule does not exist, or it is a record, which only the core withdraws.</exception>
	Task DeleteAsync(long scheduleId, CancellationToken cancellationToken = default);
}
