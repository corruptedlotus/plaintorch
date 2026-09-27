# Repository System

A single identity-keyed store of canonical entity instances, a typed repository layer over the API, and a
reactivity model that keeps every surface showing the same entity in sync — the frontend counterpart to the
core's one-authority-many-observers boundary. Specified in `PEP106` and its `Patch106.1`.

The core (this directory, `sdk.ts/plaintorch/repository/`) is framework-agnostic — plain TypeScript over the
domain SDKs, no DOM, no lit. The **binding layer** — the `EntityRef` / `EntityWatch` / `QueryRef` lit
controllers in `sipa/components/data/EntityRef.ts` — is what a component actually holds. If the system is
ever extracted as a standalone library, the split is exactly that: this directory is the package, the
controllers are a thin per-framework binding on top.

---

## Mental model

It behaves like an ORM's identity map (EF Core's `DbContext`, MikroORM's unit of work):

| ORM concept | Here |
|---|---|
| Identity map / first-level cache | `EntityStore` — one canonical instance per `{@type}:{id}` |
| `DbSet<T>` / typed access | `EntityRepository<T>` — read/write one type against the store |
| Tracked reference | `EntityRef<T>` — a component's live handle to one entity |
| Query | `QueryRef<T>` — a live filtered view over one type already in the store |
| Detached entity / working copy | `EntityDraft<T>` — a forked, isolated copy committed later |
| Change tracker flush | the write cycle (`runWrite`) + optimistic apply + rollback |

The load-bearing idea: **responses are absorbed, not returned.** Every response is walked, and each entity
node is merged into the instance already in the store (see `absorption.ts`). So every surface holding a
reference is looking at current state by construction; synchronizing them is just telling them to re-render.

---

## The pieces

**Core (`sdk.ts/plaintorch/repository/`)**
- `EntityStore` — the identity map. Absorb, peek, subscribe, per-type index, ordering guard, eviction.
- `EntityRepository<T>` — one per entity type. `get`/`refresh` (with in-flight dedup + freshness), `mutate`,
  `commit`, `fork`, `subscribe`.
- `DerivedRepository<T>` — cached views that aren't entities themselves (listings, the briefing, resolutions).
- `EntityDraft<T>` — the fork-and-commit working copy.
- `InvalidationScheduler` — declares what a change dirties and coalesces refreshes onto a microtask.
- `PlaintorchChangeFeed` — carries core-originated writes (watcher, CLI, scheduler) back to the store.
- `PlaintorchRepositories` — wires one repository per type, the derived records, invalidation, the feed, and
  the eviction sweep.
- `identity.ts` / `absorption.ts` / `equivalence.ts` / `mutation.ts` — the primitives.

**Binding layer (`sipa/components/data/EntityRef.ts`)**
- `EntityRef<T>` — resolves an entity by id, subscribes, re-renders on change, releases on disconnect. Owns
  the edit cycle (`binder`, `commit`, `fork`, `beginEdit`).
- `EntityWatch` — observes an instance *handed* to a component (a list item) rather than one it resolves.
- `QueryRef<T>` — a live, filtered, ordered slice of one type in the store.

---

## Identity — what is and isn't tracked

An entity is recognised **structurally**, not by a hard-coded list of type names: a runtime `@type` **plus a
non-empty string `id` plus a string `title`**. Key is `` `${@type}:${id}` ``.

- The `title` requirement excludes **value objects** — a dependency endpoint carries another entity's `id` as a
  *reference* but has no title, and must never be merged into that entity.
- A child record keyed on a **number** (a dependency edge) is not an entity.
- Recognising is structural; **constructing** the right class is gated on `@model` registration. A type
  identified but not `@model`-registered is absorbed as a prototype-less plain object — the failure mode that
  once silently dropped every directive. `PlaintorchRepositories` asserts at construction that every routed
  type is registered, so the two cannot drift (mirrors the core's `EntityTypeNameContractTests`).

---

## Usage scenarios

### Display one entity, by id
Use an `EntityRef` (or extend `EntityBanner`, which owns one). Never fetch in a component.

```ts
private ref = new EntityRef<Objective>(
  this,
  () => core.repos.objectives,   // which repository
  () => this.objectiveId         // which id (a thunk — it can change while mounted)
)
// render: this.ref.value, this.ref.loading, this.ref.error
```

### Display an instance handed to you (a list item)
The parent already holds the canonical instance; the child only needs to learn when it changes.

```ts
private watch = new EntityWatch(this, () => this.row.entity)
```

### Edit a field with a two-way binding — the ref binder
The one call that folds the whole edit cycle. See below.

```ts
protected binder = this.ref.binder('entity', {
  status: (o) => core.objectives.shiftWorkflow(o.id, { status: o.status }),
  '*':    (o) => core.objectives.update(o.id, o),
}, (keyPath, o, saved) => {
  if (saved && keyPath === 'title') void this.revealRenamedNote(o.id)
})
// template: <p7t-editable ${this.binder.bind('status')}> … </p7t-editable>
```

### Edit imperatively (a button, not a binding)
`repo.mutate` for an action whose optimistic edit you apply yourself; `repo.commit` when a binding already
applied it and you only need the accepted/rejected boolean.

```ts
const added = await core.repos.objectives.mutate(id, () =>
  core.polaris.addObjectiveToCurrent(id))
```

### Multi-field or cancellable edit — the fork system
Fork an isolated copy, edit freely, commit or cancel. See below.

```ts
const draft = core.repos.objectives.fork(id)
if (draft) {
  draft.value.title = 'Renamed'
  draft.value.college = ObjectiveCollege.Science
  if (draft.dirty) await draft.commit((patch, o) => core.objectives.update(o.id, patch))
}
```

### A live filtered collection — QueryRef
A view over what the store already holds, not a fetch. Load the members as usual (a listing); this keeps a
live slice of them.

```ts
private active = new QueryRef(this, core.repos.objectives,
  (o) => o.status !== ObjectiveStatus.Done,          // predicate (optional)
  (a, b) => a.title.localeCompare(b.title))           // comparator (optional)
// this.active.items → recomputed from the live store on each read
```

Predicate and comparator are read on every access, so one that closes over component state (a chosen filter)
tracks it without re-subscribing.

### A structural view built from a fixed set of kinds
When a view's *shape* depends on entity fields — a tree built from parent references restructures when any
member is reparented, which no per-identity or listing subscription reports. Subscribe to the **kinds**, not
the whole store.

```ts
this.sub = core.store.subscribeTypes(
  ['StellarDirective', 'LunarDirective', 'Objective', 'Fate', 'Decree'],
  () => this.requestUpdate())
```

`subscribeAll` still exists for a genuine whole-store need, but prefer `subscribeTypes` — a change to an
unrelated kind never re-runs a scoped view. (This is what `GridBase.observedKinds` drives.)

### A listing or aggregate — DerivedRepository
Views that aren't entities (the briefing, a listing, a note resolution) are cached under their own key and
absorbed for the entities inside, so the listing and a banner showing one of its members agree without either
refetching. Observe with a `DerivedRef`.

---

## The ref binder in detail

`ref.binder(sourceKey, persist, reaction?)` returns a two-way binder whose every edit runs the full cycle,
so a component never hand-rolls it:

1. **snapshot** the entity's fields (`beginEdit`) before the binding writes — the rollback target.
2. **write-through**: the binding applies the edit to the canonical instance immediately (on screen at once).
3. **broadcast** (`store.touch`) so every other surface showing the entity re-renders.
4. **send** via the `persist` map — a field name maps to its save call; `'*'` is the fallback (a
   whole-entity update). A failed request reports itself by returning nothing.
5. **rollback** to the snapshot if the core rejects it, and surface the failure once.
6. **reaction** (optional) runs after the write settles, for surface-specific follow-up (revealing a renamed
   note), told the field, the entity, and whether it saved.

The verbose equivalent — `new ReactiveBinder(host, key, { sourceUpdate: beginEdit, sourceUpdated: commit })` —
is still used by several banners and is exactly what the sugar folds; both go through the ref's cycle.

---

## The fork system in detail

`EntityDraft<T>` is the fork-and-commit editing mode, the alternative to a binding's immediate write.

- **Fork** (`repo.fork(id)`, or `ref.fork()`) takes a **prototype-correct copy** (built through the same
  construction absorption uses, so it's a real class instance — getters, methods, `instanceof`), plus a
  snapshot of the entity's fields at fork time: the **baseline**.
- **Edit** `draft.value` freely; nothing reaches the store until commit.
- **Commit** (`draft.commit(persist)`) diffs the copy **against the fork-time baseline** — so it sends only
  the fields the draft actually changed, and a concurrent edit to a field the draft never touched *survives*.
  It applies the diff to the canonical instance, sends it through the same write cycle as `mutate` (guard,
  invalidate on success, rollback on rejection), and is a no-op when nothing changed. Spent after one call.
- **Cancel** (`draft.cancel()`) throws the copy away; nothing was ever applied.
- `draft.dirty` / `draft.diff()` report uncommitted change.

Use it for a multi-field modal with a cancel, or a background recomputation you want to stage before
publishing. Use a binding's immediate `commit` for a single field edited in place.

---

## Propagation & ordering

- **How an edit reaches other surfaces.** A two-way binding writes straight through to the canonical
  instance, so the change is already applied; `store.touch` then notifies every subscriber. No refetch, no
  event bus.
- **Stale-response ordering.** Every read captures the store's `revision` as `issuedAt`; a local change bumps
  the revision and records `changedAt[key]`. A response is discarded if its identity changed *after* it was
  issued (`changedAt > issuedAt`) — so a slow GET can't revert an edit made while it was in flight. This is
  where correctness lives, not in the in-flight guard.
- **The change feed** carries writes made outside the client (watcher, CLI, scheduler). It announces the
  affected owners structurally, so a relationship-creating write reaches the owner even though the client's
  local dependency table can't yet see the new edge. Feed events for the changed entity are declared
  authoritative (`acceptAuthority`), overriding the local ordering claim.
- **No hand-rolled fan-out.** After a write, `mutate` already refreshes the entity and revalidates the
  observed records, and the feed reaches the owners — so the old `Promise.all([…refresh, …revalidate])` after
  every write is redundant, not a workaround. Only an owner the feed alone reaches keeps a manual nudge, and
  only for promptness when the feed is down.

---

## Bounding the map (eviction & prune)

One long-lived context, no fork-per-request-and-clear escape, so the store must actively forget — in two
halves (Patch106.1):

- **Change-ledger prune** (automatic). `changedAt` grows one marker per locally edited entity. A marker at
  revision *r* only ever discards a response *issued before r*, so once no in-flight read predates it, it can
  never drop a response again and is pruned. `beginRead`/`endRead` bracket every absorbing request; the oldest
  in-flight read is the cutoff (a low-water mark). With nothing in flight, the ledger clears. The tempting
  shortcut — drop a marker as soon as any newer read lands — is unsafe: a slower *older* read would then
  revert the edit on arrival.
- **Entity eviction** (`PlaintorchRepositories.sweep()`, on a periodic interval — `startEvictionSweep`).
  Drops records nothing needs. The trap is the **canonical-instance guarantee**: derived caches hold their
  entities' instances for the cache's life, so eviction can't go by subscribers alone. The sweep keeps
  everything a resolved derived view reaches (walked with `collectEntityKeys`) plus what the store's own
  subscribed entities nest, and drops the rest. Only entities held nowhere are freed — one fetched for a
  since-closed banner, or dropped from a refreshed listing.

Diagnostics: `store.recordCount`, `store.pendingChangeCount`, `store.inFlightReadCount`.

---

## Limitations & gotchas

- **`@model` registration is mandatory** for any tracked type, or it absorbs as a prototype-less plain object.
  Adding an entity type means an `@model('Name')` class **and** routing it in `PlaintorchRepositories`; the
  startup assertion enforces the pair.
- **Snapshots and drafts are shallow.** A snapshot (rollback target) and a fork copy share nested arrays and
  objects with the canonical instance. Edit collections through explicit API actions, never by mutating a
  draft's array — the change would leak past the draft and can't be rolled back.
- **Eviction is gated by the derived caches.** An entity a listing still holds is never evicted (correct), so
  memory is reclaimed only once the listing itself refreshes or is dropped. Bounding the derived caches
  themselves is a separate, future lever. The sweep's trigger is a plain periodic interval this version;
  idle-detected / on-refresh / memory-threshold are recorded in PEP106 for later.
- **The prune is read-driven.** A burst of pure local edits with no interleaving reads lingers until the next
  read settles. In practice reads are frequent, so the ledger stays bounded.
- **Freshness is per-repository, not global.** `EntityRepository` serves from the store within its freshness
  window (default 30s) and refetches after; `DerivedRepository` has its own (default 15s). `refresh`/`force`
  bypass it.
- **A drifted type name renders blank, loudly.** A component asking for a type no repository serves warns once
  to the console rather than failing silently — check `entityTypeName` against the runtime `@type` the core
  emits.
- **New listings must be eviction roots.** Every `DerivedRepository` must appear in `PlaintorchRepositories`'
  private `records` getter (used for both revalidation and eviction roots). A listing left out would pin
  entities the sweep can't see and evict them out from under it.

---

## Testing

Suites live beside their subject as `*.test.ts` and run under Vitest (`npm test` in `sdk.ts`). The reactive
core is pure and in-memory, so a suite needs no core process, socket, or DOM — a real `EntityStore` plus the
fakes in `test-utils.ts`. The full invariant catalogue is
`_documents/Criteria - Client SDK & Repository System.md`.
