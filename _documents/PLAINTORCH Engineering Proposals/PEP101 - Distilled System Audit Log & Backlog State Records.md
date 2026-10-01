---
status: idea
assignee: Copilot 🤖
phase: 3a
---
> [!idea]
> alright, let's give the state system a little polish:
> - from now on, an objective's "begin" is when it's set to the "Polaris" state.
> - remove start and end from directives.
> - create a new class BacklogSchedule; it has a type, state and time, and can be FK-ed to any incentive, directive or checkpoint. this will be our new scheduling class, in the stead of start and end.
> - currently type shows whether this schedule is from user planning (plan), or the reality of how things turned out (record). there might be other types later.
> - state shows lifecycle phase.
> - when adding a record with the same type and state, it replaces the old one.

# State Polish
## The objective begins at Polaris
An objective's only begin phase is now `Polaris`. `Onrush` commits it to a sprint without beginning it: a begin gate no longer refuses the shift to Onrush, a begin-triggered dependency sourced on an objective waits for Polaris, and an objective in Onrush with an unmet begin gate still has its begin ahead of it.

## Backlog schedules
`BacklogSchedule` places one lifecycle phase of one backlog item in time, and replaces the stellar directive's start and end dates as the backlog's scheduling vocabulary.
- **Type:** `Plan` (the user's planning) or `Record` (how things turned out). Stored by name, so more types can be appended.
- **State:** the lifecycle phase, `Begin` or `Finish`.
- **Time:** a civil wall-clock moment, like a due. A plan at midnight stands for the whole day.
- **Owner:** exactly one incentive (objective, fate, decree), directive (stellar or lunar) or checkpoint, each through a real foreign key. Schedules die with their owner.

An owner holds one schedule per type and state. Adding one to an occupied slot replaces the one there. This rule and the records below live in one save interceptor, `BacklogScheduleInterceptor`, which hooks synchronous saves as well as asynchronous ones, so no write pathway has to remember them.

Records are the core's to write, and a record stands exactly while its phase does. Whenever an item enters a begun status from one that is not, the core adds a `Begin` record at that moment, and a `Finish` record when it enters a finished status. Pulling the status back out of a phase withdraws its record: un-finishing drops the finish record, un-beginning the begin record. A finish implies a begin, so a jump straight from Standby to Done records both, and pulling it back to Standby withdraws both. Objectives, stellar directives, fates and checkpoints are recorded; lunar directives and decrees have no lifecycle and only take plans.

## Checkpoints are one-shot
A checkpoint has a lifecycle too: it cannot be started, only finished, and its unlock is its finish. It gets a `Finish` record when it unlocks, loses it when it re-locks, and never has a begin, recorded or planned. Checkpoints are now reconciled even in a vault with no dependency edge, so an ungated checkpoint unlocks on its own toll and condition; one with nothing to wait for is unlocked, and so finished, from the moment it exists.

## Start and end retire
A stellar directive no longer carries `startDate`/`endDate`. The database migration turns them into the directive's planned begin and finish, and the vault moves to schema v5, which drops the two keys from directive notes. Any rewrite of a note drops them too.

## Surface
`/api/schedules` lists schedules (all, or one owner's), adds a plan, and deletes a plan; records can be neither added nor deleted by hand, and a plan may not name a phase its owner never passes through (a checkpoint's begin). The SDK mirrors it as `client.schedules`. There is no UI yet.

## Open edges
Recorded in `core/.DISCUSSION.md` (State polish, 2026-09-30): planning an objective into a Polaris cycle does not consult its begin gate; nothing backfills records for items that began before this; whether the dependency ordering walk should order through a checkpoint's finish; whether a schedule should carry a time zone for CalDAV.
