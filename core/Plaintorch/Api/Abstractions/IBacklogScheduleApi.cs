using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the backlog-schedule application actions exposed by PLAINTORCH (PEP101, PEP103): when an incentive, a directive
/// or a checkpoint is planned to start or finish, and when it actually started, finished or was set aside.
/// </summary>
/// <remarks>
/// Only plans are written through this surface. Records are the core's account of how things turned out: it adds one
/// whenever an item reaches a state other than Open, and withdraws it when the item is pulled back along the main line out
/// of that state. Deferring keeps the records of how far the item got.
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
	/// No incentive, directive or checkpoint has the id; the type is unknown; the state is not Started or Finished; the type
	/// is <see cref="BacklogScheduleType.Record"/>, which only the core writes; or the owner's kind never reaches the state —
	/// a checkpoint never starts, and a fate, a decree or a lunar directive never finishes.
	/// </exception>
	Task<BacklogSchedule> AddAsync(string entityId, BacklogScheduleType type, LifecycleState state, DateTime time, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a planned schedule.
	/// </summary>
	/// <exception cref="InvalidOperationException">The schedule does not exist, or it is a record, which only the core withdraws.</exception>
	Task DeleteAsync(long scheduleId, CancellationToken cancellationToken = default);
}
