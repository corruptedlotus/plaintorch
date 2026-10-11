# Repository System

A single identity-keyed store of canonical entity instances, a typed repository layer over the API, and a
reactivity model that keeps every surface showing the same entity in sync — the frontend counterpart to the
core's one-authority-many-observers boundary. Specified in `PEP106` and its `Patch106.1`.

The core (this directory, `sdk.ts/plaintorch/repository/`) is framework-agnostic and domain-agnostic — plain
TypeScript, no DOM, no lit, and no import from outside the directory (a test in `policy.test.ts` enforces it).
What PLAINTORCH tells it lives next door, in `sdk.ts/plaintorch/repos/`. The **binding layer** — the
`EntityRef` / `EntityWatch` / `QueryRef` lit controllers in `sipa/components/data/EntityRef.ts` — is what a
component actually holds. The system is being extracted as a standalone library
(`_documents/Plan - Local Repo Separation.md`), and the split is exactly that: this directory is the package,
`repos/` is one consumer's declarations, the controllers are a thin binding on top.

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
- `ChangeFeed` — turns a server-sent event stream into invalidations, carrying writes the frontend did not
  make back to the store. Knows only a source (open a line stream) and a sink (what to invalidate).
- `RepositorySet` — creates the repositories and owns what spans them: routing by type name, invalidation,
  the feed's sink, and the eviction sweep. `set.entity(...)` and `set.derived(...)` are how a repository
  comes to exist.
- `identity.ts` / `absorption.ts` / `equivalence.ts` / `mutation.ts` — the primitives.
- `policy.ts` — the `EntityPolicy` a store is built with: what an entity is, how a payload becomes a model,
  and which of a payload's fields merge.
- `EntityStore.read` — the one call a client wraps around a request whose response is absorbed.
- `outcome.ts` — `unresolved`, what a fetcher answers when no trustworthy answer came back.

**PLAINTORCH's declarations (`sdk.ts/plaintorch/repos/`)**
- `policy.ts` — the policy the client's store is built with (`identify`, the `@model` constructor adapter,
  the unloaded-collection and unloaded-reference merge rules).
- `dependents.ts` — the table the `InvalidationScheduler` is handed: what a change to each type makes stale.
- `changeFeed.ts` — `PlaintorchChangeFeed`, the feed pointed at the core's `/api/system/changes` stream
  (watcher, CLI, scheduler writes).
- `repositories.ts` — `PlaintorchRepositories`, a `RepositorySet` declaring one repository per PLAINTORCH
  type and the derived records the UI observes. This is `core.repos`.
- `answered.ts` — the **interim** bridge that lets those repositories' fetchers tell "the core did not
  answer" from "the core says there is nothing". To be replaced by the SDK's error model, not built on.

**Binding layer (`sipa/components/data/EntityRef.ts`)**
- `EntityRef<T>` — resolves an entity by id, subscribes, re-renders on change, releases on disconnect. Owns
  the edit cycle (`binder`, `commit`, `fork`, `beginEdit`).
- `EntityWatch` — observes an instance *handed* to a component (a list item) rather than one it resolves.
- `QueryRef<T>` — a live, filtered, ordered slice of one type in the store.

---

## Identity — what is and isn't tracked

What counts as an entity is the store's **policy** — `new EntityStore({ identify, models?, mergeableKeys? })`.
The files in this directory assume no shape of their own; `identifyByShape({ typeKey, idKey?, requires? })`
builds an identifier for the common one. What follows is PLAINTORCH's policy (`repos/policy.ts`).

An entity is recognised **structurally**, not by a hard-coded list of type names: a runtime `@type` **plus a
non-empty string `id` plus a string `title`**. Key is `` `${@type}:${id}` ``.

- The `title` requirement excludes **value objects** — a dependency endpoint carries another entity's `id` as a
  *reference* but has no title, and must never be merged into that entity.
- A child record keyed on a **number** (a dependency edge) is not an entity.
- Recognising is structural; **constructing** the right class is gated on `@model` registration. A type
  identified but not `@model`-registered is absorbed as a prototype-less plain object — the failure mode that
  once silently dropped every directive. `RepositorySet.entity` refuses a type with no registered model, so
  `PlaintorchRepositories` fails at construction unless every routed
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

## What a read can say, and what the store then knows

A fetcher has three answers (`outcome.ts`), and a repository does something different with each:

| The fetcher resolves to | It means | Entity repository | Derived repository |
|---|---|---|---|
| a value | here it is | absorbed; stamped fresh | cached; notifies if it changed |
| `undefined` | the backend says there is nothing | the entity is **removed** | the record is emptied |
| `unresolved` | no trustworthy answer came back | kept as it was, still stale | last value kept, still stale, nobody notified |

So an outage keeps what is on screen, and a deletion takes it away — where both used to be the same
"nothing came back".

The store holds one of three **data states** per identity, read with `store.stateOf(key)` /
`repository.stateOf(id)`:

- `not-loaded` — nothing has answered for it yet;
- `loaded` — the canonical instance is held;
- `removed` — the backend answered that it does not exist.

`store.remove(key)` is the removal. It drops the instance, tells the identity's observers and its type's, and
is noted in the change ledger so a response issued *before* it (a listing still in flight) is discarded for
that identity instead of bringing it back. An identity absorbed again afterwards is `loaded`, as a new
instance. A change feed's `Deleted` event removes at once; the refetch that follows confirms it.

An answer of nothing does **not** remove an entity that was absorbed or changed while that request was in
flight — it is then the older of the two answers, and the identity is left stale for the next read to settle.

A derived record has no identity to remove; `repository.isResolved(key)` tells "never answered" from
"resolved to nothing".

**What held the removed entity.** This is the store's policy — `removal` on the `EntityPolicy`, one mode for
every type or a function giving one per type name:

- `"detach"` (the default) — the entity is taken out of every collection the store can reach: the collections
  of the other entities it holds, including those inside their value objects and child records, and the values
  of the derived records over it. A listing loses the row and an owner loses the member at the moment of
  removal, not at the next refetch, and whatever observes them is told. A record that *was* the entity ("the
  current one") resolves to nothing and is left stale. This is the relationship fix-up an identity map
  conventionally performs on a delete.
- `"keep"` — nothing else is touched; holders go on holding the instance until they are refreshed. For a domain
  that wants to *show* a removal rather than lose the row.

A collection that held the entity is **replaced** by a new array without it rather than spliced in place, the
way a merge replaces a collection that changed — so a surface handed the collection as a property sees a new
reference. An owner that lost a member is also left stale. Single references are never cleared: whether an
owner can do without one is not something the store can know.

**Asking about an instance you hold.** A single reference left in place still points at the removed
instance, and whatever holds it can ask: `store.wasRemoved(instance)`. The mark is on the instance (held
weakly), not on the identity, so it answers whether or not anything was observing the entity when it was
removed, for a surface that mounts afterwards, and after the identity has been absorbed again as a new
instance. `EntityWatch.removed` is this, for the entity a component was handed — which is how a surface given
`task.assignee` shows a "no longer exists" panel in its place.

**Writes.** Whether a write's result means the backend accepted it is a `WriteSuccess` test the consumer
declares — per repository, or once for a whole `RepositorySet` — and `mutate`, `commit` and a draft's `commit`
all use it. The default treats an absent result as a rejection, which suits a client that reports failure by
returning nothing; a backend whose writes answer 204 declares otherwise.

---

## Bounding the map (eviction & prune)

One long-lived context, no fork-per-request-and-clear escape, so the store must actively forget — in two
halves (Patch106.1):

- **Change-ledger prune** (automatic). `changedAt` grows one marker per locally edited entity. A marker at
  revision *r* only ever discards a response *issued before r*, so once no in-flight read predates it, it can
  never drop a response again and is pruned. `store.read` brackets every absorbing request (`beginRead` /
  `endRead` underneath); the oldest
  in-flight read is the cutoff (a low-water mark). With nothing in flight, the ledger clears. The tempting
  shortcut — drop a marker as soon as any newer read lands — is unsafe: a slower *older* read would then
  revert the edit on arrival.
- **Entity eviction** (`RepositorySet.sweep()`, on a periodic interval — `startEvictionSweep`).
  Drops records nothing needs. The trap is the **canonical-instance guarantee**: derived caches hold their
  entities' instances for the cache's life, so eviction can't go by subscribers alone. The sweep keeps
  everything a resolved derived view reaches (walked with `collectEntityKeys`) plus what the store's own
  subscribed entities nest, and drops the rest. Only entities held nowhere are freed — one fetched for a
  since-closed banner, or dropped from a refreshed listing.
- **Derived-record prune** (part of the sweep). A record nothing observes, whose value would not be served
  again anyway — stale, past its freshness window, or never resolved — is dropped before the roots are
  gathered, so it stops pinning its entities. This is what bounds a repository keyed by something open-ended
  (a path, a PUCK). An *observed* record is never pruned, however old.
- **Freshness stamps** live on the store's record (`store.markResolved` / `resolvedAt`), so they are evicted
  with the entity they describe rather than accumulating in each repository.

Diagnostics: `store.recordCount`, `store.pendingChangeCount`, `store.inFlightReadCount`.

---

## Limitations & gotchas

- **`@model` registration is mandatory** for any tracked type, or it absorbs as a prototype-less plain object.
  Adding an entity type means an `@model('Name')` class **and** routing it in `PlaintorchRepositories`; the
  startup assertion enforces the pair.
- **Snapshots and drafts are shallow.** A snapshot (rollback target) and a fork copy share nested arrays and
  objects with the canonical instance. Edit collections through explicit API actions, never by mutating a
  draft's array — the change would leak past the draft and can't be rolled back.
- **Eviction is gated by the observed caches.** An entity an *observed* listing still holds is never evicted
  (correct), so memory is reclaimed only once the listing refreshes or stops being observed. A view held
  without being observed — the array a one-shot `get()` returned, kept in a variable — is pinned only until
  its record expires: holding a view without observing it is outside the canonical-instance guarantee. The
  sweep's trigger is a plain periodic interval this version; idle-detected / on-refresh / memory-threshold are
  recorded in PEP106 for later.
- **A fetcher must say `unresolved` when it does not know.** `undefined` is taken at its word — the backend
  has nothing — and removes the entity. A fetcher over a client that returns nothing on failure has to
  translate, as PLAINTORCH's does in `repos/answered.ts`; otherwise an outage reads as a mass deletion.
- **Detaching reaches collections the store knows, and nothing else.** A removed entity leaves the listings
  and owner collections that held it, but an array a component built for itself, a single reference
  (`task.assignee`), and a child record that is meaningless without the entity it points at are all left as
  they were until refreshed. Observers are told; a surface handed its entity reads `EntityWatch.removed`.
- **A response already in flight can show a ghost.** A listing issued before a removal, landing after it, is
  not absorbed for that identity — but the raw object it carried stays in that listing until the listing is
  next refreshed.
- **The prune is read-driven.** A burst of pure local edits with no interleaving reads lingers until the next
  read settles. In practice reads are frequent, so the ledger stays bounded.
- **Freshness is per-repository, not global.** `EntityRepository` serves from the store within its freshness
  window (default 30s) and refetches after; `DerivedRepository` has its own (default 15s). `refresh`/`force`
  bypass it.
- **A drifted type name renders blank, loudly.** A component asking for a type no repository serves warns once
  to the console rather than failing silently — check `entityTypeName` against the runtime `@type` the core
  emits.
- **Create repositories through the set.** `set.entity(...)` and `set.derived(...)` are what route a type and
  make a listing a revalidation target and an eviction root. A `DerivedRepository` constructed by hand is
  invisible to the sweep, which would evict the entities it holds out from under it.

---

## Testing

Suites live beside their subject as `*.test.ts` and run under Vitest (`npm test` in `sdk.ts`). The reactive
core is pure and in-memory, so a suite needs no core process, socket, or DOM — a real `EntityStore` plus the
fakes in `test-utils.ts`. The full invariant catalogue is
`_documents/Criteria - Client SDK & Repository System.md`.
