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
One graded scale (regraded 2026-09-26, see below; the original scale was `Info < Warning < Suspended < Error <
Critical`):

```
OperationSeverity: Info < Warning < Error < Critical < Fatal
```

- **Info** — no consequence, no action needed; used very rarely. Never changes health.
- **Warning** — no breaking consequence, but best resolved to prevent further conflict (a locked file, content the
  subsystem already enforced).
- **Error** — truly invalid or illegal content the user must resolve; unresolved, an entity may not sync or may corrupt.
- **Critical** — no longer about validity: a technical failure physically preventing the subsystem from doing part of
  its job (a file it may not read, a sync that failed to apply, a root it cannot watch).
- **Fatal** — the subsystem cannot run or do its job at all; the watcher sleeps (the "degraded" modes are sleeps too)
  and retries. A fatal status can never be dismissed.

Subsystem health rolls up from the worst live status: **Fatal → Standby**, **Critical → Critical**, **Error/Warning →
Issues**, **Info/none → Ok** (wire: `ok/issues/critical/standby`, with `offline` left to clients that cannot reach the
core). A subsystem may also set a lifecycle **override** that wins over the derived rollup: the watcher forces
`Standby` while asleep and while it has no vault to serve.

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
| 6 | **Regraded 2026-09-26, superseding #2.** `Suspended` is gone — its only real producer was a locked file (now a warning) — and the scale is `Info/Warning/Error/Critical/Fatal`, one severity per reason wherever it is raised. Health gains `Critical`; `Standby` is fatal-only (and the watcher's sleep/idle override). |

## Regrading (2026-09-26)
The first scale graded by *where* an issue was noticed (a locked file was a warning when read and "suspended" when
written; a forbidden file a warning when read and an error when written), used `Critical` and `Info` nowhere, and
showed a sleeping watcher as `issues`. It is regraded by *what the issue means* for the watcher:

| Reason (operation) | Severity | Why | What the watcher does |
|---|---|---|---|
| `scan-failed` (startup-scan) | **fatal** | without its sweep it cannot trust what it observes (a broken database fails every reconcile alike) | sleeps on standby, retries the span on a 5 s → 60 s backoff |
| `vault-inaccessible` (vault-access) | **fatal** | the vault or an entity root cannot be reached | sleeps on standby, re-probes every 5 s |
| `roots-unresolved` (root, `*`) | **fatal** | no roots, no live observation at all | sleeps on standby, retries the span on the backoff |
| `fatal` (process) | **fatal** | a session fell through every guard | stays down until the next activation |
| `tick-failed` (drain-tick) | critical | a drain tick threw | carries on; clears on the next good tick |
| `discovery-failed` / `sync-failed` (reconcile) | critical | a file could not be inspected / a change could not be applied | retried per path, 2 s → 60 s |
| `permission-denied` (reconcile) | critical | the file may not be read or written | retried per path |
| `root-init-failed` (root) | critical | one root is not observed | the other roots carry on |
| `root-error` (root) | critical | an observer errored (events may be lost) | re-sweeps the vault, then resolves |
| `markdown-invalid` / `puck-violation` / `policy-violation` (reconcile) | error, or **warning** when the watcher enforced it (rewrote or purged the file) | invalid content left in the user's file | stands until the user's edit re-inspects the file (never retried on a timer) |
| `foreign-file` (reconcile) | error | an identity the vault does not recognise, left in place | stands until the file is gone or managed, or is dismissed |
| `duplicate-identity` (identity) | error | two files assert one identity | stands until one file asserts it |
| `file-in-use` (reconcile) | warning | locked by another process | retried per path until it frees up |
| `relocation-failed` (relocation) | warning | the move fast path threw | the file is inspected in place instead |
| unknown reason | critical | unclassified means technical | — |

Alongside it:
- **Content errors persist.** A conflict's "sync" only records the conflict, and used to clear the content reason the
  moment it was raised; a successful sync now clears content reasons only when it enforced them (a rewrite or a
  purge). Content reasons are not retried on a timer — the user's edit re-inspects the file.
- **Sleep is standby.** The watcher forces `Standby` while asleep; "degraded startup mode" (live observation without
  a sweep) is gone — a failed sweep, like unresolvable roots, is fatal and the watcher sleeps and retries the span.
- **Sessions start clean.** The live status set is reset when a vault session starts and ends, so a fatal status of
  one vault can never hold another on standby.
- **Durable rows.** `OperationStatusEvent` severities are stored by name; a legacy `Suspended` row reads back as a
  warning (no migration).
- `IsCritical` / `CriticalIssueCount` on the wire now mean *critical or fatal*.

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
- **Retry hints** — whether a retried status (a locked file, a sleeping watcher) should carry its next retry for the client.
- **Database health** — the watcher has no probe of its own database; a broken one surfaces as a fatal `scan-failed`.
- **Cross-subsystem rollup** — a global health surface once a second subsystem (migrations) adopts the core.
