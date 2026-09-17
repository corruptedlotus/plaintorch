---
status: implemented
patches:
  - Patch098.1 - Add Elapsed Time Tracking
assignee: Copilot 🤖
phase: 2a
---
# Working Time Unit
The time unit specification for work items allow a minute-based stamp, which is translated to timespan/time-only in the application layer and to hours + fraction in the UI. 
# Executives: Time Allocations
Executives have 3 optional time allocations:
- ### Estimation
  The main allocation parameter. This parameter acts as a "progress" for the task. It's up to the user to track this accurately or treat it as a dummy progress holder. In each Polaris Cycle, the sum of the estimations indicate the total "workload" of the day.
- ### Min & Max
  These are additional optional parameters that can be set as time allocations. they are pretty self explanatory. If either is set before estimation is specified, estimation will take their value too. Estimation cannot be more than max and less than min, if it is when min or max are set, it clamps it.

# Patches
## Patch098.1 - Add Elapsed Time Tracking
- Added an `Elapsed` property to executives: the raw count of tracked (passed) minutes, expressed as a whole-minute working time unit.
- Unlike the estimation/min/max allocations, `Elapsed` is a running tally rather than a target. It defaults to `0` and is never clamped by the allocation reconciliation (`NormalizeTimeAllocations`).
- Surfaced through the Polaris executive update API (`ExecutiveUpdate`) only. It is intentionally *not* part of executive planning: a freshly planned executive has not been worked yet, so its tracked minutes always start at `0`. On update, `null` leaves it unchanged and any supplied value (including `0` to reset) overwrites it; there is no `Clear` flag because it is never unset.
- Carried onto the objective API's executive projections and exposed on the TypeScript client SDK's `Executive`, `PolarisExecutivePlan`, and `ExecutiveUpdate`.
- Backed by the `ExecutiveElapsed` EF migration, which adds a non-nullable `Elapsed` column with a default of `0`.
