---
status: accepted
assignee: Claude 🤖
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
- **Phase 4 (storage-mode policy objects)** is the real prerequisite for *clean policy reporting*: today the
  mode policy's `Decide` returns `(VaultSyncAction, string Reason)`; the status system wants a typed reason +
  severity + involved paths. Phase 4 is where `Decide` should return structured checks, deleting the
  `HasPolicyViolation` string-matching. Until then, the watcher adapter ships an **interim** policy-reason
  mapping, clearly flagged, that Phase 4 swaps out without changing the framework.
- **Phase 3 (family/classification)** removes the other heuristic (`HasPuckViolation` / name-lists). Secondary.
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
- **Phase C — Typed failures.** Replace exception-message sniffing with typed outcomes from the discovery/sync
  services; validation issues (`candidate.Issues`) become checks directly.
- **Phase D — Structured policy outcomes.** Co-lands with REFACTOR Alpha Phase 4: the mode-policy `Decide`
  returns structured checks; the watcher's policy report is built from them.
- **Phase E — API + frontend.** Generalise `WatcherIssueReport` to an operation-status contract (severity,
  involved files, reason code, resolved timestamp, history), keep the watcher endpoints, add history queries,
  enrich the SDK/health surface. Frontend deliverable: an **Obsidian status-bar icon** reflecting the rolled-up
  core/watcher health (`Ok/Suspended/Issues/Offline`), with a **hover tooltip** (reusing the existing tooltip
  component) listing the active statuses and their severity. The icon reads the health rollup and the tooltip
  the active-status list; both come from the operation-status contract, so the icon generalises beyond the
  watcher as other subsystems adopt the core.

## Core types (`Pleiades.Diagnostics`)
- `OperationSeverity` — `Info/Warning/Suspended/Error/Critical`.
- `OperationCheck(ReasonCode, Passed, Severity, Files?, EntityId?, Detail?)` — one evaluated check.
- `OperationReport(OperationId, ScopeKey, Checks)` — an operation run's full result.
- `OperationStatus(...)` — a live flagged status keyed `(OperationId, ScopeKey, ReasonCode)` with occurrence
  count and first/last timestamps.
- `OperationStatusTransition(Kind, ...)` / `OperationStatusTransitionKind` — Raised/Escalated/De-escalated/Resolved.
- `OperationHealth` — `Ok/Suspended/Issues/Offline`.
- `OperationStatusRegistry` (singleton) — `Ingest(report) → transitions`, `GetActive*`, `GetRecentResolved`,
  `GetHealth`, `SetHealthOverride`.
- `IOperationStatusSink` + `OperationStatusReporter` (singleton) — reporter ingests into the registry and
  forwards transitions to the sink.
- Durable: `OperationStatusEvent` (EF entity, `Pleiades.Vault.Database`), `OperationStatusEventBuffer`
  (buffered `IOperationStatusSink`) + `OperationStatusPersistenceWorker` (`Pleiades.Plaintorch.Diagnostics`).

## Open questions / future
- **Retention** of the durable log (age/count/milestone-based) — deferred, like the graveyard/audit retention.
- **Batched vs per-transition** persistence — Phase A buffers and drains; batching thresholds can tune later.
- **Suspension source** — whether "suspended" statuses should carry an expected-retry hint for the client.
- **Cross-subsystem rollup** — a global health surface once a second subsystem (migrations) adopts the core.
