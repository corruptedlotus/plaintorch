---
status: in progress
assignee: Claude 🤖
phase: "5"
---
# Refactor Beta & Sunnyside Mk1

## Refactor BETA — Vault write ownership (queue & drain)

### The problem: two implementations of the entity↔file map

The vault has two directions (see `core/Vault/Watcher/.GENESIS.md`): files instruct the core (discovery/inference)
and the core reflects onto files (sync). Today the **on-disk placement of an entity is computed twice**:

- **Write side** — `PlaintorchMarkdownStorageService` (`SaveCanonicalMarkdownAsync`, via `VaultStoragePathComposer`),
  invoked synchronously, in-request, by the API services after they commit the entity. Reparent is reconstructed
  from a `previous` clone (`IsReparented(entity, previous)`).
- **Read side** — discovery (`VaultMarkdownDiscoveryService` + `VaultWatcherPathPolicy`), which resolves a note's
  owner by where the parent's file actually is.

The file-*mutation* primitive is already shared (both the API path and the watcher's own `VaultWatcherSyncService`
delegate to `SaveCanonicalMarkdownAsync`). What diverges is the **trigger and the placement orchestration**, and
that divergence is the bug: for identity-driven (Freeform/Implicit) entities and out-of-root parents the two answers
drift, and the watcher misbehaves on reparenting / freeform notes kept away from their default location.

Evidence — five pinned repros (`core.tests/Core/{ReparentingRelocationTests,FreeformOutOfRootEditTests,PhysicalRelocationNoAftershockTests}.cs`):

1–3. A new / reparented child of a directive kept at `Projects/Campaign` lands under the parent's *canonical* folder
`Directives/Campaign/…` (freshly conjured) instead of the parent's real folder. (Write composes the parent's declared
directory; discovery resolves the parent's actual one.)
4. Renaming a freeform (Quiet) directive never reaches its self-named folder, so the title reverts on next read
(`FreeformVaultStorageModePolicyService.ResolveWriteTargetPath` anchors to the existing path).
5. Moving an implicit note to a non-partition subfolder inside its own parent deletes the entity (the startup orphan
pass keys the begun boundary on its recorded path).

### The principle

**One owner for the entity↔file projection.** The core cedes file-write authority; it records a minimal *intent* and
the watcher drains it — the same component that reads files, so write placement is read placement, by construction.
The parity the test suite enforces by hand becomes true structurally. The queue is how the core hands off authority.

### The design

**Minimal intent.** The queue is a *dirty-set*, not an event log: an intent means "reconcile entity X to its current
DB state" or "remove entity X by its last-known identity." The drainer re-derives everything from the DB + the file
(the body lives only in the file), so no change-kind taxonomy, diff, or payload is stored. Reparent falls out as
move-by-identity for free.

**Durable, transactional outbox (an EF table).** Persisted, not in-memory — that is the only way to get write-side
crash/offline parity (a crash between DB-commit and file-write reconciles at next startup). Kept clean by outbox
semantics, mirroring `OperationStatusDismissalRecord` (natural key ⇒ idempotent upsert, single-delete clear):

```
[PrimaryKey(EntityType, EntityId)]           // one row per entity — coalesced; normally zero rows
VaultWriteIntent { EntityType, EntityId, Kind (Reconcile|Remove), Identity?, LastKnownPath?, EnqueuedUtc }
```

- One row per entity (coalesced). Deleted on successful drain. No attempt/backoff columns — a failed drain raises a
  PEP108 tier-3 status and is retried by the existing `WatcherRetryScheduler` (`watcher-crash-resilience`).
- Recorded in the **same transaction** as the entity mutation (enqueue before the API's `SaveChangesAsync`).
- `Identity`/`LastKnownPath` exist only so a `Remove` can locate a file whose DB row is already gone.

**The drainer.** A single `VaultEntityWriteDrainer` reconciles one entity's file to its DB state via the storage
service, then deletes the row. It must: locate the current file **by identity** (read the body before re-emitting —
also what makes move-by-identity work), drain **serialized per entity** (one drain per entity at a time; a coalesced
re-dirty triggers a follow-up pass), and drain in **dependency order** (parent before child, as lore `SetIndex`
needs). A startup drain re-drives leftover rows (offline/crash recovery); the watcher's loop drains rows enqueued
while a client isn't waiting.

**Synchronous, bounded drain + `noteReady` (Part 2).** API-originated writes drain synchronously *inside the request*
so the HTTP 200 is the "file is ready" signal (see the audit below — every read-after-write dependency is satisfied
by this). A bounded wait (`Task.WhenAny(drain, Delay(timeout))`, default 2s, **timeout sourced from a user
preference**) caps worst-case latency: on timeout the response carries `noteReady:false`, the drain continues (the
durable row guarantees completion), and the client shows "the note will be available shortly" and polls
`resolve-note`. The 2s bounds only the client response, never the drain's own internal read-your-writes. Transient
drain failures (locked file) → `noteReady:false` + retry; permanent failures → a PEP108 status.

### Read-after-write audit (what requires the synchronous drain)

The whole request/response surface is safe — no API service reads its own write or returns a note path; the SDK is
pure HTTP. The dependencies that require the drain to have *completed* are:

- **Core (inside the write pipeline):** body/frontmatter preservation reads the previous on-disk file
  (`PlaintorchMarkdownStorageService`); begin-boundary's audit is gated on `File.Exists`; lore `SetIndex`'s chained
  descendant rewrites read the already-moved parent; the startup migration→reconcile phases assume prior files are on
  disk.
- **Client (Obsidian plugin only):** the seven entity banners re-open the note after a rename
  (`getFileByPath(path)!` throws if absent); `entityActions.createEntityNote` opens after `begin` (`openLinkText`
  creates a phantom if absent). All already hold the path from the API before opening.
- **A second core writer** exists outside the storage service: `PlaintorchEngine.WriteVaultMarkdown` (single-command /
  import) writes with `File.WriteAllText` directly. To truly "deny the core file ops" it must route through the queue
  too, or be carved out as a CLI-only path.

### Parts

- **Part 1 — write-side placement converges on read-side discovery, behind the durable outbox foundation.**
  - 1a. *Done* (`925e0d1`): `ResolveCanonicalPathAsync` composes a child beneath its parent's real folder (resolved by
    the parent's identity); `GetFilePathUnderParentDirectory` + per-shape `GetFilePathInContainer`. Flips repros 1–3.
    (Also fixed a pre-existing test-harness SQLite-pool flake surfaced by the added I/O.)
  - 1b. *Done*: the `VaultWriteIntent` outbox table (durable, composite-keyed, coalesced) + `VaultWriteQueue`
    (record-in-transaction / synchronous in-request drain / `DrainPendingAsync` startup recovery) + a generic
    `PlaintorchMarkdownStorageService.SaveEntityAsync`. Live on the directive **Reconcile** write path
    (create / update / reparent / workflow-shift / set-icon / set-banner): each records an intent atomically with the
    entity change, then drains it. `PlaintorchEngine.Initialize*` drains leftover intents. Delete and the other entity
    types keep calling `Save*Async` directly during transition (Part 2). Tests: `VaultWriteQueueTests` (a write leaves
    no pending intent; a crash-left intent is recovered by the startup drain).
- **Part 2 — the bounded drain, the remaining write paths, and the client `noteReady` handling.**
  - 2a. *Done*: the bounded drain. `VaultWriteQueue.DrainReconcileAsync` waits for the inline drain only up to
    `WatcherPreferences.NoteQueueTimeout` (PEP116, default 2000 ms, merged in): within it the file is on disk and the
    write reports **ready**; past it it reports **pending** while the drain finishes on best-effort (the durable row /
    startup drain guarantees completion). Inline on the request scope, one SQLite connection (a fresh-scope drain
    deadlocks the same DB), via `Task.WaitAsync`. `noteReady` is returned but not yet surfaced to the client.
  - 2b. *Done (reconcile writes)*: every entity type's reconcile writes — directive / objective / fate / decree /
    onrush sprint / executive order / polaris cycle / lore page — route through `VaultWriteQueue.WriteAsync`
    (record-in-transaction + `SaveChanges` + bounded drain, replacing `SaveChangesAsync` + a direct `Save*Async`).
    Lore's `SetIndex` subtree chain (a synchronous read-your-own-writes move) and the delete paths stay direct for now.
    *Pending*: delete/Remove (a `Remove` intent + a drain that archives the file by last-known identity).
  - 2c. *Server done*: `noteReady` rides the `X-Note-Ready` response header — the bounded drain marks a per-request
    `VaultWriteReadiness` on timeout, and `NoteReadinessEndpointFilter` (on the shared enriched API group) sets the
    header to `false` when pending (absent = ready). *Pending*: the SDK reads the header and the client acts — the
    seven banners' rename-reveal and `createEntityNote` show "the note will be available shortly" and poll
    `resolve-note` before opening.
- **Part 3 — the remaining two repros.** *Done*: (repro 4) `FreeformVaultStorageModePolicyService.ResolveWriteTargetPath`
  rebases onto the authored container with the entity's current base name (`RebaseOntoAuthoredLocation`), so a Quiet
  rename renames the self-named folder/file in place instead of leaving a stale, self-reverting name; (repro 5) the
  discovery orphan pass skips a begun boundary whose identity is still asserted by a scanned file — a move within the
  vault is not a deletion — so a note relocated inside its parent survives the sweep. All five pinned repros are green.
- **Part 4 — deny the core direct file writes:** delete `SaveCanonicalMarkdownAsync`'s in-request invocation from the
  API path and the `IsReparented`/`previous`-clone reconstruction; fold `PlaintorchEngine.WriteVaultMarkdown` in;
  consolidate the stray direct FS *readers* (note-resolver frontmatter fallback, media, direct dir-name readers)
  behind a watcher read facade (the read-side half of "watcher as the sole vault-IO interface").

### Acceptance criteria

The five pinned repros go green (1–3 in Part 1a, 4–5 in Part 3) — **all green as of Part 3**; a crash-recovery test (an
intent row left undrained is reconciled by the startup drain) — **green**; the full suite stays green and stable across
repeated (parallel) runs — **402 passed / 0 skipped, stable**.

### Status log

- 2026-09-21 — Part 1a landed (`925e0d1` on `claude/watcher-reparenting-tests`): placement convergence; repros 1–3
  green; suite 398 passed / 2 skipped, stable across repeated runs.
- 2026-09-21 — Part 1b landed: `VaultWriteIntent` outbox + `VaultWriteQueue` + startup drain, live on the directive
  Reconcile write path; crash-recovery test green; suite 400 passed / 2 skipped, stable across repeated runs.
- 2026-09-21 — Part 3 landed: freeform (Quiet) rename rebases onto the authored location; the orphan pass treats a
  still-asserted moved note as a move, not a deletion. All five repros green; suite 402 passed / 0 skipped, stable.
- 2026-09-22 — merged `claude/user-preferences` (PEP116) in; Part 2a landed: the bounded drain reads
  `WatcherPreferences.NoteQueueTimeout`. Suite 415 passed / 0 skipped.
- 2026-09-22 — Part 2b (reconcile writes) + 2c (server) landed: all entity types route through `VaultWriteQueue.WriteAsync`;
  `X-Note-Ready` header emitted via `VaultWriteReadiness` + `NoteReadinessEndpointFilter`. Suite 415 passed / 0 skipped.

## Sunnyside Mk1

_TBD — tracked separately in this PEP; not part of the Refactor BETA work above._
