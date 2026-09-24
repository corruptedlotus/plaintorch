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
  (toast/dialog via the in-house `p7t-toast`/`p7t-modal`, bundled icons; navigation is interim no-op) + injects the
  bridge `core` client.

Two inversions carry it:
- **`PlatformHost`** — the SIPA-first seam the UI depends on instead of `obsidian`. Sketch: `toast(message, kind?)`;
  a **modal service** that hosts our `ModalBase` / `SuggestModalBase` bases (so swapping the underlying dialog engine never
  touches the 17 dialog classes); `navigate(target)`; an **icon provider** (`getIcon`/`getIconIds`); `activeFile()`
  (Obsidian editor context; SIPA returns none for now). Vault reads/writes go through the `core` client (watcher-
  synced), not the host.
- **`core` provider** — the UI reads an injected client set at bootstrap (e.g. a module-level `setCore()` mirroring how
  `window.app` is set today), replacing the direct `@pleiades/sdk/plaintorch/node` import. Obsidian bootstrap injects
  the node client; SIPA injects the bridge client.

### Decisions pinned (planning run, P1 and the Spike)
- **Shared-code home — extract `@pleiades/sipa` (at `sipa/`) up front.** *(Named at P1; the planning run's working
  name was `@pleiades/client` at `client.ts/`.)* The relocation of `components/` (+ `orbits/`) into the
  package lands *first* (mechanical move, plugin stays green, still transitively importing `obsidian` until the
  inversion), so all subsequent inversion work happens in the code's final SIPA-first home. (Note the sequencing
  consequence: extraction alone does **not** unblock SIPA — the SIPA renderer can only consume the package once the
  `obsidian` imports are gone, i.e. after the `PlatformHost` inversion.)
- **Dialog host — in-house on native `<dialog>`, functionality from 3MO controllers** *(decided by the Spike; the
  planning run had left it open between `@3mo/dialog` and a bespoke host)*. The modal service hosts our own
  `p7t-modal` (modals) and `p7t-toast` (toasts), drawn with our own markup and `p7t-icon`. Wherever the behaviour is
  something the 3MO suite already provides as a **controller**, the host uses that controller rather than hand-rolling
  it: `@3mo/focus-controller` (focus-within, focus return), `@3mo/slot-controller` (footer/action presence),
  `@3mo/pointer-controller` (hover-pausing a toast), `@3mo/interval-controller` (toast timer ticks). No 3MO
  *components* (`mo-dialog`, `mo-snackbar`, `mo-button`, `mo-icon`) — see *Spike findings* for why.
- **Pickers — in-house `SuggestModalBase` on `@3mo/navigability`** *(decided by the Spike)*. The 9 pickers keep their
  `getSuggestions` / `renderSuggestion` / `onChooseSuggestion` / `onClose` contract; the base draws an input plus the
  results as its own `p7t-suggest-modal`, with the cursor owned by 3MO's `NavigabilityController` (the list controller). **Grid
  is intrinsic**: a picker declares `layout: 'list' | 'grid'` (today `SelectMediaModal` gets its grid by injecting
  `plaintorch-media-selector` into Obsidian's result container); grid mode switches the controller to
  `orientation: 'both'` and moves Up/Down a whole row through its `handleKeyDown` hook.
- **Naming** *(pinned at P5)*. `p7t-` is for tag names only. Classes: `XxxBase` is the abstraction, `Xxx` its plain
  implementation, `SomethingXxx` a specific one; tags follow as `p7t-xxx` / `p7t-something-xxx`; CSS classes are
  `plaintorch-*`; custom properties `--p7t-*`. Hence `ModalBase` / `SuggestModalBase` (what the dialog classes
  extend) and, in the SIPA host, the elements `Modal` (`p7t-modal`), `SuggestModal` (`p7t-suggest-modal`) and
  `Toast` (`p7t-toast`). `SelectStatusModal` stops being an abstract intermediate with five near-identical
  subclasses: it becomes one concrete picker taking its status table (P6).
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
- **Spike (gate for P5/P6) — 3MO dialog viability.** Probe `@3mo/dialog` + the 3MO toast (there is no
  `@3mo/notification`; it is `@3mo/snackbar`) and decide the modal, toast and suggest hosts. *(done — in-house hosts
  on native `<dialog>` + 3MO controllers; see Decisions and *Spike findings*)*
- **P2 — `PlatformHost` + `ObsidianHost`.** Define the interface; route `getApp`/`navigateToEntity`/`Notice`/`Modal`/
  `getIcon` through it; implement the Obsidian adapter. Plugin green, no behavior change.
- **P3 — `core` provider inversion.** UI consumes injected `core`; Obsidian bootstrap injects the node client. Fix the
  barrel + the 3 direct importers.
- **P4 — Toasts.** `host.toast(...)` over the host; Obsidian→`Notice`, SIPA→`p7t-toast` (in-house, Notice-like stack
  at the top-right; pointer-controller pause, interval-controller timer). Refactor the 89 sites (mechanical,
  batchable).
- **P5 — Modals.** `ModalBase` base over the modal service; migrate the 8 plain modals. Obsidian→`Modal`,
  SIPA→`p7t-modal` (native `<dialog>` + `showModal()`: top layer, backdrop, Escape through the `cancel` event; a
  promise-based open/confirm; focus/slot controllers).
- **P6 — SuggestModals.** `SuggestModalBase` base on `NavigabilityController` with intrinsic list/grid layout; migrate the 9
  pickers (`SelectMediaModal` drops its injected grid class for `layout: 'grid'`).
- **P7 — Icons + navigation.** Icon provider (bundle lucide; Obsidian→`getIcon`); SIPA `navigate` = no-op + toast.
  After this the package no longer imports `obsidian`. *(done — the icon and navigation half landed with P2, the
  last `obsidian` import left with P6; what remains is the SIPA side, built with P8.)*
- **P8 — SIPA renderer mount (payoff).** In `standalone/`: depend on `@pleiades/sipa` (package.json + esbuild, and
  keep the renderer's own lit imports on the package's copy); `SipaHost` + inject the bridge `core` + mount the
  briefing (± dependency canvas) into the renderer, replacing the status-only view; wire the change feed / eviction
  sweep. Styling: a SIPA stylesheet defining the Obsidian theme variables the components read, and lift the
  component-facing part of the plugin's `styles.css` (`@property --flare-intensity`, the `--p7t-accent-*` tokens)
  into the package so both hosts share it.

P1 is the structural prerequisite; the Spike gates P5/P6; P2–P3 are the foundation; P4–P7 are largely independent and
reorderable; P8 is the realization. Obsidian keeps working throughout because every seam ships its `ObsidianHost` impl
in the same phase.

### Spike findings (3MO dialog viability, 2026-09-24)
Probed in a real renderer (Electron 44, offscreen): stock `mo-dialog`, a restyled `mo-dialog` subclass, `mo-snackbar`
stock and restyled, and a prototype picker on `NavigabilityController`.

- **Styling reach was not the problem.** A `mo-dialog` subclass (≈40 lines of CSS + two template overrides) matched an
  Obsidian modal closely, and the snackbar restyled to a top-right Notice. Behaviour was sound too: native `<dialog>`
  in the top layer, `DialogComponent.confirm()` resolving on the primary action and rejecting on Escape/close, and no
  lit-application `Application` root needed (`Application.topLayer` falls back to `document.body`).
- **The costs were.**
  - **Bundle: +394 KB unminified / +240 KB minified (≈ +27% on the 1.44 MB plugin).** The dialog itself is 16 KB; the
    rest is `@material/web` (104 KB), `@a11d/lit-application` + its router, `urlpattern-polyfill` and `path-to-regexp`
    (≈96 KB), `reflect-metadata` (42 KB) and the 3MO button/selection stack (≈60 KB).
  - **`reflect-metadata` patches the global `Reflect`**, which inside Obsidian is shared with every other plugin.
  - **`@3mo/icon` loads Material Icons from `fonts.googleapis.com` at runtime** (an `@import` in a style element it
    appends to `document.head`). Offline — or on first open, before the font arrives — icons render as their ligature
    words: the stock dialog's close button reads "close", the snackbar reads "info … close".
  - **Version drift:** current 3MO (`@3mo/button` 1.2 → `@3mo/indexability` 0.2.1) requires `@a11d/lit` ≥ 0.13; the
    package is on 0.11.1, so a second `@a11d/lit` copy is bundled (0.11.1 lacks `ElementRefs`).
  - The restyle replaced everything visible anyway (heading, close button, buttons, surface, placement) and the
    snackbar's stack layout assumes bottom anchoring (a top-anchored stack pushes older toasts off-screen). What 3MO
    would really contribute is a thin native-`<dialog>` wrapper, the confirm/cancel promise plumbing and a timer.
- **The controllers are the useful part, and fit 0.11.1.** `focus-`, `slot-`, `pointer-` and `interval-controller`
  import only what `@a11d/lit` 0.11.1 exports. `@3mo/navigability` 0.1.0 runs on 0.11.1 **provided `@3mo/indexability`
  is pinned to `0.2.0`** (0.2.1 is the one needing `ElementRefs`/0.13; 0.2.0 has the `item`/`itemAt`/`observe` API
  navigability uses). Cost ≈ 21 KB. The prototype picker verified, with real key events: disabled rows skipped both
  ways, the cursor re-found by key after filtering, Enter choosing, focus kept in the input with
  `aria-activedescendant` on it, grid Left/Right by cell and Up/Down by a whole row (column count read off the
  rendered layout), Escape closing the native dialog.
- **Prototype technique worth keeping for P6:** the input keeps the keys and forwards only the cursor keys to
  `navigability.handleKeyDown(e)`; `keyboardTarget` is a *getter* returning the rendered input or `null` (null at
  construction, so the controller attaches no listener of its own; later it is where `aria-activedescendant` lands);
  `orientation` is a getter too (options are built once, at construction, before attributes apply). Beware a
  reconnect: `hostConnected` then finds the input and attaches the controller's own listener, doubling the
  forwarding — P6 must pick one route.

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
- ~~3MO dialog/notification viability~~ — resolved by the Spike: in-house hosts on native `<dialog>`, 3MO controllers.
- ~~Final name/location of the shared package~~ — resolved at P1: `@pleiades/sipa` at `sipa/`.
- **`@a11d/lit` 0.11 → 0.13 (deferred).** Current 3MO has moved to 0.13 (`ElementRefs`); SIPA stays on 0.11.1 with
  `@3mo/indexability` pinned at `0.2.0`. Upgrading is its own verified step (the package, `@3mo/popover`/`tooltip`
  and their tooltip hacks) — not a SIPA Mk1 prerequisite.
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
- 2026-09-24 — Spike done (no code landed; the probes live outside the repo). `@3mo/dialog` + `@3mo/snackbar`
  restyle well but cost +394 KB, a global `Reflect` patch, a runtime Google Fonts dependency and a second
  `@a11d/lit`. Decided: in-house `p7t-modal` / `p7t-toast` on native `<dialog>` using 3MO controllers; pickers
  in-house on `@3mo/navigability` (+ `@3mo/indexability` pinned `0.2.0`) with intrinsic list/grid, verified on
  `@a11d/lit` 0.11.1. Findings above.
- 2026-09-24 — P2 landed: `sipa/host/` defines `PlatformHost` (`toast`, `navigation`, `media`, `icons`, optional
  `globalContexts`) with `provideHost` / `getHost` and a forwarding `host` proxy; `obsidian/src/host/ObsidianHost.ts`
  implements it and `onload` installs it first. Routed through it: note navigation (`navigateToEntity`,
  `openEntityNote`, `openNotePath`, `openNoteWhenReady`, and a new `followRenamedNote` replacing the seven banner
  copies of the active-editor check), media URLs (`resolveMediaUrl`/`resolveMediaIcon` lose their `App` argument;
  one shared `isImageSource`), `p7t-icon` and the icon catalog (`host.icons`, so the icon half of P7 is done), and the
  `.p7tpx` save (`host.globalContexts`; the canvas hides "Save to file" without it). The dead banner `app` property
  and its assignments are gone. Package files importing `obsidian`: 48 → 36 (Notice, the dialogs and their `App`
  handles remain). Plugin esbuild green, `tsc` 46 identical per file; package typecheck 36.
- 2026-09-24 — P3 landed: the core client is injected. `sipa/components/data/coreProvider.ts` holds `provideCore` /
  `getCore` and a forwarding `core` proxy (settled over lit context: ~10 plain function modules and the dialogs use
  `core` outside any element tree, and 23 `DerivedRef` fields read `core.repos` at construction). The barrel's
  `core` now comes from the provider, so the 50 `import { core } from '..'` sites are untouched; `FullBanner`,
  `NoteBanner` and `EntityRef` (whose `EntityWatch`/`QueryRef` read the store) no longer import the node client. The
  plugin calls `provideCore(plaintorchNodeCoreClient)` right after `provideHost` in `onload` and drops its lazy
  client lookups. A browser-platform bundle of the package no longer reaches `@pleiades/sdk/plaintorch/node`. SDK:
  `NodeSocketPlaintorchCoreTransport` moves to `plaintorch/nodeTransport.ts`, exported as
  `@pleiades/sdk/plaintorch/node-transport` (for the shell's main process, without the default client the node
  entry constructs); the transport types and `toLines` are public; the per-call debug log no longer throws on an
  empty or non-JSON body. SDK 110/110, plugin green, `tsc` 46 identical.
- 2026-09-24 — P4 landed: the package's 80 remaining `new Notice(…)` sites (the 81st, the note-pending notice,
  moved into `ObsidianHost` in P2) are `toast(message, kind)` through the host — a TypeScript-AST codemod keyed on
  the per-site inventory: 41 error, 22 success, 15 warning, 2 info; the five mixed success/failure messages take their
  kind from the same condition as their text. `ObsidianHost.toast` stays a plain
  Notice (kinds ignored), so Obsidian behaves as before. Package files importing `obsidian`: 36 → 19 (the dialogs and
  their `App` handles). Plugin green, `tsc` 46 identical; package typecheck 36.
- 2026-09-24 — P5 landed: `sipa/host` adds the dialog contract (`DialogHost` → `createModal` / `createSuggest`,
  views and shells) and the two bases, `ModalBase` and `SuggestModalBase`, which keep the shape the dialogs were
  written against (`contentEl`, `open`/`close`, `onOpen`/`onClose`; `setTitle` for `titleEl.setText`; `plaintorch-root`
  and the post-close clearing done once in the base), plus `createChild` for Obsidian's `createEl`.
  `obsidian/src/host/obsidianDialogs.ts` hosts them on Obsidian's own `Modal` / `SuggestModal`, so the plugin's dialogs
  look and behave as before. The 8 modals extend `ModalBase` and lose their `App` parameter (`EntityEditModal.forEntity`
  and `openOccurrenceModal` too; 11 call sites). `PromptTextModal` drops Obsidian's `Setting` for a plain input and a
  `mod-cta` button row, with a `confirmLabel` (the global-context save now says "Save"). Plugin green; `tsc` 46 with
  messages identical to P1.
- 2026-09-24 — P6 landed, and with it P7's goal: **the package no longer imports `obsidian`.** The pickers extend
  `SuggestModalBase` (Obsidian's `createEl` → `createChild`, the injected global `sleep` → an imported one).
  `SelectStatusModal` is one concrete picker taking its status table (`SelectStatusModal.prompt(objectiveStatusDescriptors)`)
  instead of an abstract base with five table-only subclasses; `ChangeStateModal`, a sixth copy, is folded into it —
  `ObjectiveItem`'s notch prompts and shifts the workflow itself, and now offers *Failed* (the old
  `Object.keys(typeof ObjectiveStatus)` never did). The media picker asks for `layout = 'grid'` (the plugin maps it to
  `plaintorch-suggest-grid`, the renamed `plaintorch-media-selector` rule) and uses the base's `setQuery`. `getApp` is
  gone; the package drops its `obsidian` and `@types/node` dev dependencies and the plugin its `obsidian` path pin —
  the package's own typecheck gains no error without them (36 → 25: only unused locals removed). Plugin green; `tsc`
  46 → 35, nothing new; no package module in the bundle touches `obsidian`.
- 2026-09-24 — SIPA host landed (`@pleiades/sipa/hosts/sipa`, an export path the main entry never re-exports, so
  neither it nor `lucide` reaches the plugin bundle): `createSipaHost({ mediaUrl, mediaScheme })`; `Modal`
  (`p7t-modal`) and `SuggestModal` (`p7t-suggest-modal`) on native `<dialog>` with `ModalShell` / `SuggestModalShell`
  behind `DialogHost` — the picker's cursor is `NavigabilityController` (list, or grid with Up/Down by row), the
  toast (`Toast` in a `ToastStack`) counts down on `IntervalController` and holds on hover through
  `PointerController`, and the stack is a manual popover re-raised per toast so it shows above an open modal; lucide
  glyphs from `lucide@1.47.0` (aliases resolve, `lucideNames()` for the picker); the shell stylesheet (`sipaStyles`)
  maps Obsidian's variable names onto the `--p7t-*` palette with accent `#71549c`. Opening a note toasts that SIPA
  has no editor yet; following a renamed note is silent; no `globalContexts`. The component-facing rules of the
  plugin's `styles.css` (`@property --flare-intensity`, the `--p7t-accent-*` tokens) moved into the package
  (`componentStyles`, adopted by both hosts). Pins: `@3mo/navigability@0.1.0`, `@3mo/indexability@0.2.0`,
  `@3mo/interval-controller@0.0.5`, `lucide@1.47.0` exact. Plugin green, `tsc` 35, bundle free of the SIPA host.
- 2026-09-24 — **P8 landed: the standalone shell shows the real UI.** A briefing window (`briefing.html` /
  `briefing.js`, its own bundle) installs `createSipaHost` + a bridge-backed core client, starts the change feed and
  the eviction sweep, and mounts `p7t-briefing` — the plugin's own — once a vault is active and swept. The bridge was
  made whole: JSON bodies, forwarded `x-note-ready` (every write used to throw after the core applied it), and
  streaming (`core:stream-*`, relayed by `core-streams.ts` over the SDK's pooled socket transport, closed on
  ask / reload / destroy). Vault files load through `plaintorch-media://vault/…` (served from the served vault,
  traversal refused). Tray: "Open briefing" first; click, double-click and a relaunch open it; a visible launch ends
  in it. Windows never navigate away. One AppUserModelID (`pleiades.plaintorch`). `pt-*` tags and `--pt-*` tokens are
  `p7t-*`, accent `#71549c`. **Verified end to end** in an offscreen Electron harness running the real
  `briefing.html`, preload, transport, stream relay and media protocol against a scratch core + vault: the briefing
  renders seeded data; the settings modal opens and Escape removes it; the status picker walks by keyboard and a
  choice writes (`/workflow`, no rollback, success toast); an external rename reaches the open window in ≤ 1 s via the
  feed, also after a reload; the media picker opens as a grid above a modal, searches the lucide catalog, moves by
  row, and sets a lucide and a vault-image icon (both written, both shown); `plaintorch-media` serves vault images and
  refuses `../` escapes; the prompt dialog creates an objective; "Open note" toasts that SIPA has no editor yet.
