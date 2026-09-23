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
  - 2b. *Done*: every entity type's reconcile writes route through `VaultWriteQueue.WriteAsync` (record-in-transaction +
    `SaveChanges` + bounded drain), and deletes through `RecordRemoveAsync` + `DrainRemoveAsync` — a durable `Remove`
    intent committed with the database removal, then a synchronous file archive (a delete has no note to open, so it is
    not bounded); the startup drain recovers a crashed `Remove` from its captured last-known path. Covers directive /
    objective / fate / decree / executive order / polaris / lore. Two flows stay direct by design: lore's `SetIndex`
    subtree chain (a synchronous read-your-own-writes move), and the onrush sprint-cascade delete (its milestone-cycle
    logic needs a precise commit ordering that `DrainRemove`'s own `SaveChanges` would disturb).
    `Directive`/`Objective`/`Declarative`/`PolarisCycle` API services now hold no direct
    `PlaintorchMarkdownStorageService` dependency. Tests: `VaultWriteQueueTests` (a delete archives the file and leaves
    no intent; a crashed remove intent is recovered by the startup drain).
  - 2c. *Done*: `noteReady` rides the `X-Note-Ready` response header — the bounded drain marks a per-request
    `VaultWriteReadiness` on timeout, and `NoteReadinessEndpointFilter` (on the shared enriched API group) sets the
    header to `false` when pending (absent = ready). The SDK transport exposes response headers; the core client keeps
    a `lastWriteNotePending` flag (set per non-GET write, untouched by reads, so it survives a re-resolve) — pinned by
    `noteReadiness.test.ts`. The client consumes it through one shared `openNoteWhenReady(path, pending)` helper that
    announces "the note will be available shortly" and polls the vault for the file before opening (in a new tab once
    it lands) — replacing the seven banners' `getFileByPath(...)!` + `openFile` (which threw on a not-yet-indexed file)
    and `createEntityNote`'s eager `openLinkText` (which spawned a phantom). Even a ready write is indexed a beat after
    it lands, so this also removes that latent race.
- **Part 3 — the remaining two repros.** *Done*: (repro 4) `FreeformVaultStorageModePolicyService.ResolveWriteTargetPath`
  rebases onto the authored container with the entity's current base name (`RebaseOntoAuthoredLocation`), so a Quiet
  rename renames the self-named folder/file in place instead of leaving a stale, self-reverting name; (repro 5) the
  discovery orphan pass skips a begun boundary whose identity is still asserted by a scanned file — a move within the
  vault is not a deletion — so a note relocated inside its parent survives the sweep. All five pinned repros are green.
- **Part 4 — deny the core direct file writes.** *Done.*
  - **Second writer removed.** `PlaintorchEngine.WriteVaultMarkdown`/`WriteMarkdown` and the create/save methods that
    reached them were dead code (no callers — the CLI `Program.cs` uses only `InitializeVault*` + `GetStatusReport`),
    so the engine is reduced to vault activation and holds no file writer. The API path (through the queue) is now the
    only way an entity mutation reaches a markdown file.
  - **Reparent detection no longer reconstructs from a `previous` clone.** `IsReparentedFromFile` derives it from the
    existing file's own location (the same containment resolution the watcher reads on the way in) versus the entity's
    current declared parent, so a write that omits a `previous` clone still reparents correctly — the fragility the
    clone-based `IsReparented` carried is gone. `previous` now survives only as (a) an optional path hint for the one
    case a same-identity scan cannot cover — the onrush placeholder→real **id change** — and (b) audit "previous"
    detail; it no longer drives placement. (Verified against the full reparent/rename/onrush/survival/relocation suites.)
  - **Read side — assessed; a facade is deliberately not built.** The "stray direct readers" were already consolidated
    by REFACTOR Alpha: containment is one walk-up (`VaultWatcherPathPolicy.TryResolveContainingDirectiveId`, which
    `IsReparentedFromFile` now reuses) and `MarkdownFileLocator.TryGetContainingOnrushSprintId` is the single onrush
    reader. The remaining direct reads (note resolution, media) are localized, legitimate domain reads over different
    concerns; a unifying "watcher read facade" over them would be an abstraction without payoff. The read-side goal is
    already met by the existing single-owner resolvers.

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
- 2026-09-22 — Part 2c (client) landed: SDK surfaces the header (`lastWriteNotePending`, `noteReadiness.test.ts`);
  the 7 banners + `createEntityNote` open through the shared `openNoteWhenReady` (announce + poll for the file). SDK
  109 tests green; plugin builds.
- 2026-09-22 — Part 2b delete/Remove landed: deletes route through `RecordRemoveAsync` + `DrainRemoveAsync`; startup
  drain recovers a crashed remove. Four API services shed their direct storage dependency. Suite 417 passed / 0 skipped.
- 2026-09-22 — Part 4 landed: the second writer (`PlaintorchEngine.WriteVaultMarkdown` + dead create/save methods) is
  removed; reparent detection is file-based (`IsReparentedFromFile`), no longer reconstructed from a `previous` clone;
  the read facade was assessed and deliberately not built (already consolidated). Suite 417 passed / 0 skipped, stable.
  **Refactor BETA is complete.**

## Sunnyside Mk1 — the Sunnyside Interface for Pleiades Affairs (SIPA)

SIPA is PLAINTORCH's own standalone (Electron) client — the non-Obsidian face of the vault, living in `standalone/`.
Refactor BETA made the **core** vault-authoritative (the watcher is the sole vault-IO and the daemon owns the files);
SIPA makes the **client** stand on its own so the whole planning UI runs without Obsidian. The ambition is to invert
the current arrangement: the reusable UI becomes **SIPA-first** (platform-neutral), and Obsidian is demoted to *one
adapter* among two.

### Why now / what already exists
- The daemon, the vault, and the watcher exist independently of Obsidian; the files are the vault, and edits from any
  source are reconciled by the watcher (Refactor BETA). So "no Obsidian" does not mean "no vault".
- `standalone/` is already a working Electron scaffold: main process + tray + `core-process` (spawns the daemon) +
  `CoreTransport` (HTTP over the profile's named pipe / unix socket) + a preload **bridge** + a renderer that already
  constructs `new PlaintorchCoreClient({ transports: [new BridgeTransport()] })`. **Transport is solved.** The renderer
  today shows only a splash/status view — the payoff is mounting the real client UI into it.
- The reusable UI (`obsidian/components/`) is framework-agnostic **lit web components** (briefing dashboard, entity /
  occurrence items, cards, editables, dependency canvas). They render into plain DOM; only a handful of seams tie them
  to Obsidian.

### The coupling (two axes, not one)
1. **The Obsidian host API.** 54 files import `obsidian` (48 in `components/`, 6 in `src/`). Concentrated seams:
   - **App handle** — `getApp() = (window as any).app` (`components/editing/index.ts`) + ~10 direct `window.app` reads;
     every modal-open and navigation needs it.
   - **Dialogs** — 17 classes extend `Modal` (8: Executive, Occurrence, EntityEdit, EntityDetail, OnrushDetail,
     LunarDirective, Preference, PromptText) or `SuggestModal` (9 fuzzy pickers: SelectCollege, SelectTimeframe,
     SelectStatus, SelectEndpoint, SelectObjective, SelectMedia, ChangeState, plus the inline AddObjective picker in
     `BriefingCardOnrush` and AddToOnrush picker in `ObjectiveBanner`). *(Recounted at P1; the planning run's
     "21 / 7" was a miscount.)*
   - **Toasts** — `Notice`, **89 call sites**.
   - **Icons** — `PleiadesIcon` → `getIcon()` (Obsidian's bundled lucide registry; backs *every* `p7t-icon`, so
     pervasive); `iconCatalog.ts` → `getIconIds()`; `main.ts` → `addIcon()` (the custom glyph).
   - **Navigation / vault** — `navigateToEntity` → `workspace.openLinkText`, `openNoteWhenReady`, `getActiveFile`,
     `TFile`, `normalizePath`.
2. **The hardcoded node core client.** Components consume `core = plaintorchNodeCoreClient` (a `node:http` client) —
   via the `components/index.ts` barrel and three direct importers (`FullBanner`, `NoteBanner`, `EntityRef`). The SIPA
   renderer is **sandboxed** (no Node; it talks through the preload bridge), so it cannot use the node client. `core`
   must become an *injected* dependency.
3. **Hidden couplings** (invisible to an `obsidian`-import count; found at P1):
   - **Theme CSS variables** — the components read ~22 of Obsidian's theme tokens by name (~330 uses:
     `--text-normal`, `--interactive-accent`, `--background-primary`, `--font-interface`, `--text-error`, …).
   - **HTMLElement prototype helpers** — Obsidian patches `createEl` / `createDiv` / `empty` / `setText` onto every
     element. Used only inside the dialog classes, so they disappear with P5/P6.
   - **The plugin's global `styles.css`** — besides view-host rules it defines two things the components rely on:
     the `@property --flare-intensity` registration (the animated item flare) and the `--p7t-accent-polaris` /
     `--p7t-accent-onrush` tokens on `.plaintorch-root`.

Excluded from SIPA scope (stay Obsidian-only): the **on-note banner** (`PageBannerRenderer`/`CustomBanner`, editor
extension + markdown post-processor) and the **note command palette** ("Initialize directive/objective from current
file"). These are intrinsically editor-bound.

### Target architecture
- **`@pleiades/sipa` (at `sipa/`) — a new shared package** (extracted from `obsidian/components/` +
  `obsidian/orbits/` + `obsidian/assets/` + the new host), a sibling of `@pleiades/sdk`, bundled from source by both
  consumers. It holds the platform-neutral lit UI, the
  `PlatformHost` interface, and the injected-`core` provider. It depends on `@pleiades/sdk`, `@a11d/lit`, `@3mo/*`,
  lucide — **never on `obsidian`** (once the inversion completes).
- **Obsidian = one adapter** (`obsidian/`): the plugin shell (`src/main.ts`, the `ItemView`/`TextFileView` hosts, the
  on-note banner, the command palette) + an **`ObsidianHost`** implementing `PlatformHost` (App/Notice/Modal/getIcon/
  openLinkText) + injecting the node `core` client.
- **SIPA = the other adapter** (`standalone/`): the renderer mounts `@pleiades/sipa`, provides a **`SipaHost`**
  (toast/dialog/icons via the chosen libraries; navigation is interim no-op) + injects the bridge `core` client.

Two inversions carry it:
- **`PlatformHost`** — the SIPA-first seam the UI depends on instead of `obsidian`. Sketch: `toast(message, kind?)`;
  a **modal service** that hosts our `P7tModal` / `P7tSuggest` bases (so swapping the underlying dialog engine never
  touches the 21 dialog classes); `navigate(target)`; an **icon provider** (`getIcon`/`getIconIds`); `activeFile()`
  (Obsidian editor context; SIPA returns none for now). Vault reads/writes go through the `core` client (watcher-
  synced), not the host.
- **`core` provider** — the UI reads an injected client set at bootstrap (e.g. a module-level `setCore()` mirroring how
  `window.app` is set today), replacing the direct `@pleiades/sdk/plaintorch/node` import. Obsidian bootstrap injects
  the node client; SIPA injects the bridge client.

### Decisions pinned (this planning run)
- **Shared-code home — extract `@pleiades/sipa` (at `sipa/`) up front.** *(Named at P1; the planning run's working
  name was `@pleiades/client` at `client.ts/`.)* The relocation of `components/` (+ `orbits/`) into the
  package lands *first* (mechanical move, plugin stays green, still transitively importing `obsidian` until the
  inversion), so all subsequent inversion work happens in the code's final SIPA-first home. (Note the sequencing
  consequence: extraction alone does **not** unblock SIPA — the SIPA renderer can only consume the package once the
  `obsidian` imports are gone, i.e. after the `PlatformHost` inversion.)
- **Dialog host — OPEN, deliberately.** Start the modal service on **`@3mo/dialog`** for the 8 plain modals and
  **`@3mo/notification`** for toasts (only `@3mo/popover` + `@3mo/tooltip` are deps today). But the modal service is an
  abstraction: if 3MO's styling proves too restrictive we **retract to a bespoke dialog host** without touching the 21
  dialog classes. **The 7 `SuggestModal` pickers stay undecided** pending the spike — likely in-house on the existing
  `SelectBase` / `p7t-popover` / `fuzzy.ts` if 3MO can't stretch. Gated by an early spike (below).
- **Interim navigation — no-op + "no editor yet" toast.** SIPA has the vault on disk but no in-app editor yet, so
  `navigate(entity)` just toasts that the editor is coming. (A real in-app read/edit surface is a later effort.)
- **Branch — `claude/sipa-first`, off the current consolidated tip** (`dev/phase2d` at `2db63a5`). Isolates this
  cross-cutting refactor.
- **Theme — keep Obsidian's CSS variable names** *(pinned at P1)*. The components go on reading `--text-normal`,
  `--interactive-accent`, … by name; a non-Obsidian host *defines* those variables in its own stylesheet. No
  package-owned token rename.
- **Package mechanics** *(pinned at P1)*:
  - The package **owns the UI dependencies** (`@a11d/lit`, `@3mo/*`, dagre, deferred-promise) in its own
    `node_modules`; the plugin no longer lists them. esbuild follows the `file:` link to the real path, so package
    code resolves lit from `sipa/node_modules` — one lit runtime, as long as a host never imports lit from a second
    copy of its own (relevant to the SIPA renderer at P8, which imports `@a11d/lit` directly today).
  - Imports inside the package are **relative**; the old bare aliases (`components/…`, `orbits`, `assets/…`) are
    gone, so no host needs `paths` entries for package internals. Hosts import from `@pleiades/sipa` only.
  - The package has its **own `tsconfig.json`**, which esbuild applies to the package's files (nearest tsconfig per
    file); it pins `experimentalDecorators: true` + `useDefineForClassFields: false`.
  - Transitional: `obsidian` and `@types/node` are package dev dependencies for its own `typecheck` (the barrel
    still exports the node `core` client). The plugin's `tsconfig` pins the `obsidian` module to its own copy so its
    program holds a single set of Obsidian types.

### Phased plan (Obsidian esbuild build green at every gate)
- **P0 — Plan + branch.** This section; cut `claude/sipa-first` from the current tip. *(done)*
- **P1 — Extract `@pleiades/sipa`.** Move `components/` + `orbits/` + `assets/` into the package; add its
  package.json/tsconfig; rewire `obsidian/` to consume it (as it already consumes `@pleiades/sdk` from source). The
  package may still `import { … } from "obsidian"` transitionally. No behavior change; plugin green. *(done —
  `standalone/` is wired at P8 instead, since nothing there consumes the package before the mount.)*
- **Spike (gate for P5/P6) — 3MO dialog viability.** Stand up a `@3mo/dialog` + `@3mo/notification` probe inside the
  package (mind `@3mo/theme`'s global-import side-effects vs the plugin's styles). Decide: 3MO for modals? 3MO or
  in-house for suggest? Records the choice before the modal phases commit.
- **P2 — `PlatformHost` + `ObsidianHost`.** Define the interface; route `getApp`/`navigateToEntity`/`Notice`/`Modal`/
  `getIcon` through it; implement the Obsidian adapter. Plugin green, no behavior change.
- **P3 — `core` provider inversion.** UI consumes injected `core`; Obsidian bootstrap injects the node client. Fix the
  barrel + the 3 direct importers.
- **P4 — Toasts.** `host.toast(...)` over the host; Obsidian→`Notice`, SIPA→`@3mo/notification`. Refactor the 89 sites
  (mechanical, batchable).
- **P5 — Modals.** `P7tModal` base over the modal service; migrate the 8 plain modals. Obsidian→`Modal`, SIPA→(spike
  result).
- **P6 — SuggestModals.** `P7tSuggest` base; migrate the 9 pickers per the spike decision.
- **P7 — Icons + navigation.** Icon provider (bundle lucide; Obsidian→`getIcon`); SIPA `navigate` = no-op + toast.
  After this the package no longer imports `obsidian`.
- **P8 — SIPA renderer mount (payoff).** In `standalone/`: depend on `@pleiades/sipa` (package.json + esbuild, and
  keep the renderer's own lit imports on the package's copy); `SipaHost` + inject the bridge `core` + mount the
  briefing (± dependency canvas) into the renderer, replacing the status-only view; wire the change feed / eviction
  sweep. Styling: a SIPA stylesheet defining the Obsidian theme variables the components read, and lift the
  component-facing part of the plugin's `styles.css` (`@property --flare-intensity`, the `--p7t-accent-*` tokens)
  into the package so both hosts share it.

P1 is the structural prerequisite; the Spike gates P5/P6; P2–P3 are the foundation; P4–P7 are largely independent and
reorderable; P8 is the realization. Obsidian keeps working throughout because every seam ships its `ObsidianHost` impl
in the same phase.

### Verification (per phase)
Obsidian `esbuild.config.mjs production` green + no net-new plugin `tsc` errors over baseline; SDK `npm test` (vitest);
`standalone` `typecheck`/`build`; browser smoke where a controller changed ([[obsidian-plugin-verification]]). No core
(C#) changes are expected — SIPA is client-only; the daemon/transport already exist.

### Non-goals / deferred
- **In-app editor** — SIPA edits notes via the OS/watcher later; for Mk1 navigation is a no-op + toast.
- **On-note banner + note command palette** — remain Obsidian-only (editor-bound).
- **Pure-browser target** — out of scope; SIPA is Electron, so the node/bridge transport already covers it (a browser
  target would additionally need the daemon to expose HTTP/WS, which it does not today).

### Open items to resolve during implementation
- 3MO dialog/notification styling reach vs `@3mo/theme` global side-effects (the Spike answers this; may flip the modal
  and/or suggest host to bespoke).
- ~~Final name/location of the shared package~~ — resolved at P1: `@pleiades/sipa` at `sipa/`.
- The exact `core`-injection mechanism (settable module singleton vs lit context) — settle in P3.

### Status log

- 2026-09-24 — P0: `claude/sipa-first` cut from `dev/phase2d` (`2db63a5`). Gate baseline: plugin esbuild green,
  plugin `tsc` 46 pre-existing errors, SDK vitest 110/110.
- 2026-09-24 — P1 landed: `obsidian/components`, `obsidian/orbits`, `obsidian/assets` and `orbit-humanize.tsx`
  moved to `sipa/` (history-preserving renames); 38 internal alias imports made relative; the plugin imports
  `@pleiades/sipa` (5 sites). The package lockfile was seeded from the plugin's so every dependency kept its exact
  version (a fresh resolve would have bumped `@a11d/lit`, `@3mo/popover`, `@3mo/theme` by a patch). Verified: the
  production bundle is **byte-identical** to the baseline once the `../sipa/` module-path comments are normalised
  (one lit runtime, `obsidian` still external); plugin `tsc` 46 errors, identical per file; the package's own
  `typecheck` reports exactly the 36 of those that sit in its files.
