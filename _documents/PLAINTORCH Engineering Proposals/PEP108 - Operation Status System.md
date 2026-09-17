---
status: implemented
assignee: Claude 🤖
phase: 2a
---
# Operation Status System

A subsystem needs to tell a client, per running operation, what is currently *wrong or suspended*, how bad it
is, which files/entities are involved, and — crucially — *when it recovered*. The watcher is the first and
motivating consumer, but the mechanism is general: migrations, orbit materialization, and reconciliation all run
long, fail per-item, and recover, and none of them should reinvent this.

This PEP defines a domain-neutral **operation status core** (`Pleiades.Diagnostics`) driven by *structured
operation outcomes*, and adopts it in the vault watcher, replacing the existing string-heuristic issue system.

## Operational goals
- Track **per-operation, per-scope, per-reason** failures and suspensions as first-class, deduplicated statuses.
- Grade them on a single severity scale that includes **suspension** (not just failure).
- Carry the **files and entity** involved, not a single path.
- Make **resolution** observable and durable — you can see *when* a status recovered, across restarts.
- Instrument by **reporting facts**, not by hand-marking/resolving flags in control flow.

## Background: what exists, and why it is being replaced
A watcher issue system already exists and is, in effect, a first draft of the "named + parametrised code
checkpoint" idea: `VaultWatcherIssueSignal` (a `VaultWatcherIssueType` + named `Criterion`/`ResolutionCriterion`
+ `ScopeKey` + `Path` + `Detail`) observed via `VaultWatcherIssueRegistry.Observe(signal, criterionSatisfied)`,
surfaced through `/api/system/watcher/issues[-for]` and the system briefing. It works, but it has structural
problems that make it the wrong base to build on:

1. **The resolve side drifts.** Because a success must *manually* re-resolve every criterion it might have
   failed, happy paths are littered with `ObserveSuccess` calls (the "candidate is null" branch resolves seven
   criteria by hand). Miss one and a status is stale forever. This is the classic failure mode of hand-placed
   checkpoints.
2. **Failures are classified by string-matching.** `ClassifyOperationalFailure` sniffs exception messages;
   `HasPuckViolation`/`HasPolicyViolation` scan `candidate.SuggestedReason` for substrings (`"policy"`,
   `"disallow"`, `"freeform"`). This is the "recognise by string, not structure" anti-pattern being removed
   elsewhere (REFACTOR Alpha), and it exists *only because* the pipeline returns a reason string instead of a
   structured outcome.
3. **Criticality is a boolean**, hardcoded per type — no graded levels, no distinct "suspended" state.
4. **One path per issue**; **no resolution history** (resolved issues are deleted); **in-memory only**.
5. The `IssueType`/`Criterion`/`ScopeKey` identities overlap confusingly, and `GetCriterionSummary()` is
   degenerate (`Total == Failed`).

## The model
The shift is from *hand-marked checkpoints* to *structured outcomes projected into status*:

- Each time an **operation** runs against a **scope**, it emits one **`OperationReport`**: the operation id, the
  scope key, and the full set of **checks** it evaluated this run — *each pass or fail*, with a reason code,
  severity, involved files, optional entity id, and detail.
- A single **`OperationStatusReporter`** hands the report to the **`OperationStatusRegistry`**, which *diffs* it
  against what is currently flagged for that `(operation, scope)`:
  - a failed check not yet flagged → **Raised**;
  - a failed check already flagged, severity changed → **Escalated/De-escalated** (same severity → occurrence
    bump only, no transition);
  - a check that now **passes** but was flagged → **Resolved**.
- The registry returns the resulting **transitions**; the reporter forwards them to a durable **sink**.

The operation never says "raise flag X". It reports *what it checked and the result*; the core derives
raise/lower by diffing. That is what makes lowering **automatic** — the success path carries no un-flag
bookkeeping, so it cannot drift. The convention that makes auto-resolution complete: **an operation reports all
of its checks each run** (pass and fail); a reason code simply absent from a report is left untouched (it belongs
to a different operation).

### Identity and deduplication
A live status is keyed **`(operationId, scopeKey, reasonCode)`**. Two different reasons on the same file are two
statuses; resolving one leaves the other. Re-failures of the same key bump an occurrence count and refresh the
newest detail without creating duplicates.

### Severity and health
One graded scale, suspension included:

```
OperationSeverity: Info < Warning < Suspended < Error < Critical
```

Subsystem health rolls up from active statuses: any `Error`/`Critical` → **Issues**; else any `Suspended` →
**Suspended**; else **Ok**. A subsystem may also set a lifecycle **override** (e.g. the watcher forcing `Offline`
when it stops, or `Suspended` when cancelled) that wins over the derived rollup — preserving today's
`Ok/Issues/Standby/Offline` behaviour.

### Durability
Two stores, per the decision below:
- a fast in-memory **registry** for current state + rollup + a bounded recent-resolution ring (immediate "when
  did it resolve" within the session);
- a durable append-only **log** (`OperationStatusEvent`) written **once per transition** (Raised / Escalated /
  De-escalated / Resolved) — *not* per observation — so history survives restarts while staying bounded by
  distinct incidents rather than observation frequency. Occurrence counts live in the registry.

## Decisions
| # | Decision |
|---|---|
| 1 | **General core, watcher first.** The core lives in a domain-neutral `Pleiades.Diagnostics`; the watcher is adapter #1. Migrations / orbit / reconciliation adopt the same core later. |
| 2 | **Single graded severity scale including `Suspended`** (`Info/Warning/Suspended/Error/Critical`), rather than a separate lifecycle axis. Health rollup still distinguishes suspension. |
| 3 | **Both live and durable.** In-memory registry for current state + rollup; durable `OperationStatusEvent` log of transitions for cross-restart history. |
| 4 | **Dedup key = operation × scope × reason.** Distinct reasons on one scope are distinct statuses; this is why a report must carry the full evaluated check-set for auto-resolution to be exact. |
| 5 | Name: **operation status** (not "checkpoint" — `Checkpoint` is the PEP102 dependency entity). |

## Relationship to REFACTOR Alpha
The status system's *quality* depends on operations returning structured outcomes — which is what REFACTOR Alpha
is building. None of REFACTOR Alpha *blocks* the core (it is new, additive code), but:
- **Phase 4 (storage-mode policy objects)** was the real prerequisite for *clean policy reporting* — **done**:
  `Decide` now returns a typed `VaultSyncDecision`, and phase D (above) deleted the `HasPolicyViolation`
  string-matching that the watcher adapter carried as an interim mapping until the policy objects existed.
- **Phase 3 (family/classification)** removed the other heuristic (`HasPuckViolation` / name-lists) — also folded
  into the phase-D typed concern.
- **Phase 2 (path composition)** is independent.

## Implementation plan
- **Phase A ✅ — Status core (additive, no behaviour change).** `Pleiades.Diagnostics`: severity, check,
  report, status, transition, health, `OperationStatusRegistry` (diff + rollup + resolved ring), `IOperationStatusSink`,
  `OperationStatusReporter`; durable `OperationStatusEvent` entity + EF migration + a buffered sink and a
  persistence worker. Tested: dedup (op×scope×reason), auto-resolve diff, escalation, rollup incl. suspended,
  durable Raised→Resolved round-trip.
- **Phase B ✅ — Watcher adopts the core.** `VaultWatcherIssue*` (registry, signal, type, issue, criterion,
  health enum) and the ~11 hand-marked `Observe` calls are gone: a `WatcherOperations` catalog + a
  `WatcherStatusReporter` build one `OperationReport` per stage, and the system API maps the live registry onto
  the unchanged `WatcherIssueReport`/`SystemBriefing` wire contract (health `Ok/Suspended/Issues/Offline` →
  `ok/standby/issues/offline`). One deliberate, PEP-sanctioned behaviour change: a locked file (`file-in-use`)
  now surfaces as a first-class **suspension** (`standby` health) rather than a non-critical issue that left
  health `ok`. The exception/candidate classification heuristics are carried over unchanged, to be removed in C/D.
  The instrumentation is now unit-testable (it was only reachable through the live background service before).
- **Phase C ✅ — Typed failures.** Exception-message sniffing is gone: a hard markdown failure throws a typed
  `MarkdownDeserializationException`, and file-access failures are classified structurally by
  `VaultFileAccessException.TryClassify` (exception type + OS error code / HResult, not English text), with the
  inspect read routed through `VaultFileAccess.ReadAllTextAsync` so its failures are typed at the boundary.
  Validation issues (`candidate.Issues`) were already checks directly. (The `SuggestedReason` PUCK/policy
  heuristics remained here until phase D replaced them with a typed concern.)
- **Phase D ✅ — Structured policy outcomes.** The last string-heuristic is gone. The mode-policy `Decide` now
  returns a `VaultSyncDecision(Action, Reason, Concern)` carrying a typed `VaultSyncConcern`
  (`None/MarkdownInvalid/PuckViolation/PolicyViolation`) that classifies the single root concern of the decision —
  a rejection driven by identity is a puck concern, one driven by placement/ownership is a policy concern (subsuming
  incidental validation issues on an unknown file), a rewrite/conflict driven purely by content is a markdown
  concern, and clean/benign decisions carry none. The candidate carries the concern; `WatcherStatusReporter`
  reads it (falling back to a markdown floor when raw validation issues exist under no higher concern) and
  `HasPuckViolation`/`HasPolicyViolation` — the `SuggestedReason` substring sniffing — are deleted. This realises
  decision #4: **one root cause yields one classified reason** (the reporter still emits the whole reconcile
  check-set each run, now derived from the typed concern, so auto-resolution stays exact). The `Reason` string
  survives only as human-readable detail. Pinned by `StorageModeDecisionMatrixTests` (a per-mode concern matrix) and
  the un-skipped `WatcherStatusTests.One_bad_file_should_raise_a_single_classified_issue`.
- **Phase E 🚧 — API + frontend.** _Landed:_ `WatcherIssueRecord` gained graded `severity` and involved `files`
  (C# + SDK), keeping the existing endpoints/contract; and the **Obsidian status-bar indicator**
  (`p7t-watcher-status`) — a health-colored dot (`ok/standby/issues/offline`) + active-issue count, with a hover
  tooltip (reusing `p7t-tooltip`) listing the active statuses and their severity badges. It polls
  `GET /api/system/watcher/issues` and both the dot and tooltip read the operation-status contract, so it
  generalises beyond the watcher as other subsystems adopt the core. _Deferred:_ resolved-timestamp exposure and
  a history-query endpoint (the durable `OperationStatusEvent` log already records the raised→resolved timeline;
  no consumer needs the query yet).
- **Dismiss feature ✅ — snooze an issue.** A user can dismiss a status so it stops counting toward health and
  nagging. A dismissal is durable (`OperationStatusDismissalRecord` EF table, loaded into the registry when a vault
  session activates) and matches **by value**, so it needs no runtime bookkeeping to expire: an `Instance` dismissal
  captures the status detail as a **fingerprint** and matches only while that holds, so a *different problem* on the
  same file+reason (the detail changes) — or a resolve→re-raise — lifts the snooze, while an identical problem
  (including one re-raised after a restart) stays snoozed. `OperationStatusRegistry` gains `Dismiss`/`Restore`/
  `IsDismissed`/`LoadDismissals` and dismissal-aware `GetHealth`; `OperationStatusDismissalService`
  (`Pleiades.Plaintorch.Diagnostics`) is the write-through bridge; the API exposes
  `POST /api/system/watcher/issues/dismiss|restore` (+ SDK), and `WatcherIssueRecord` carries a `dismissed` flag
  (counts and health exclude dismissed). Dismissal scope is a pinned framework — `Instance` (surfaced) plus reserved
  `File` ("always ignore this file") and `Reason` ("always ignore this issue"), so those broaden the snooze with a
  UI + endpoint change, not a schema change. Frontend: the status indicator's hover tooltip became an interactive
  **`p7t-popover`** (a reusable click-to-open, top-layer, light-dismissing popover) whose rows carry Dismiss/Restore,
  with dismissed issues in a collapsed section.
- **Foreign-file warning ✅ — the first dismiss-native status.** Its motivating consumer: the identity-driven,
  non-exclusive modes (Freeform/Implicit) now leave an unrecognised-PUCK file **in place** (`Ignore`) and raise a
  `foreign-file` **Warning** (`VaultSyncConcern.ForeignFile`) instead of purging it as an Error — aggression stays
  confined to Enforced (granted) territory. Unlike an actionable reason, this advisory is *standing*: a successful
  (no-op) sync does not clear it (`ReportSyncSucceeded` excludes it), only a clean re-inspection or a user dismissal
  does. This closes the last watcher repro (`Foreign_file_in_a_non_exclusive_root_should_surface_as_a_warning`) — the
  watcher suite now carries zero skips.

## Core types (`Pleiades.Diagnostics`)
- `OperationSeverity` — `Info/Warning/Suspended/Error/Critical`.
- `OperationCheck(ReasonCode, Passed, Severity, Files?, EntityId?, Detail?)` — one evaluated check.
- `OperationReport(OperationId, ScopeKey, Checks)` — an operation run's full result.
- `OperationStatus(...)` — a live flagged status keyed `(OperationId, ScopeKey, ReasonCode)` with occurrence
  count and first/last timestamps.
- `OperationStatusTransition(Kind, ...)` / `OperationStatusTransitionKind` — Raised/Escalated/De-escalated/Resolved.
- `OperationHealth` — `Ok/Suspended/Issues/Offline`.
- `OperationStatusRegistry` (singleton) — `Ingest(report) → transitions`, `GetActive*`, `GetRecentResolved`,
  `GetHealth`, `SetHealthOverride`; and the dismiss surface `Dismiss`/`Restore`/`IsDismissed`/`LoadDismissals`/
  `ClearDismissals` + `GetActiveStatusesWithDismissal`.
- `OperationStatusDismissal(Scope, OperationId, ScopeKey, ReasonCode, Fingerprint, DismissedUtc)` +
  `OperationStatusDismissalScope` (`Instance/File/Reason`) — a value-matched dismissal (`Matches(status)`).
- `IOperationStatusSink` + `OperationStatusReporter` (singleton) — reporter ingests into the registry and
  forwards transitions to the sink.
- Durable: `OperationStatusEvent` + `OperationStatusDismissalRecord` (EF entities, `Pleiades.Vault.Database`),
  `OperationStatusEventBuffer` (buffered `IOperationStatusSink`) + `OperationStatusPersistenceWorker`, and
  `OperationStatusDismissalService` (write-through dismissal bridge) — all in `Pleiades.Plaintorch.Diagnostics`.

## Open questions / future
- **Retention** of the durable log (age/count/milestone-based) — deferred, like the graveyard/audit retention.
- **Batched vs per-transition** persistence — Phase A buffers and drains; batching thresholds can tune later.
- **Suspension source** — whether "suspended" statuses should carry an expected-retry hint for the client.
- **Cross-subsystem rollup** — a global health surface once a second subsystem (migrations) adopts the core.
