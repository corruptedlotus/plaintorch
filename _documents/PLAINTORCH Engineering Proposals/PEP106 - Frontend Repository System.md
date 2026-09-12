---
status: implemented
assignee: Copilot 🤖
phase: 2A
patches:
  - Patch106.1 - Tracked References, Forks & Queries
---
# Frontend Repository System
The frontend has no shared notion of "the objective with this PUCK". Every component that displays an entity holds its own private copy, fetched independently and mutated independently. Two surfaces showing the same entity are two unrelated objects, and a change to one is invisible to the other.

This PEP introduces a **repository system**: a single identity-keyed store of canonical entity instances, a repository layer that is the only path to the API, and a change feed that carries core-originated mutations back to every surface. It is the frontend counterpart to what the vault↔database boundary already does on the core side — one authority, many observers.

## Motivation
Four distinct failures, all currently worked around by hand.

**Instance-local state.** `EntityBanner` holds `@state() entity`, `EntityItem` holds `@property() entity`, `Briefing` holds `@state() data`. `ChangeStateModal` writes its result into `host.entity` — that one component updates and every other view of the same entity goes stale. There are roughly eleven hand-written `this.entity = await core.X.get(id)` refetch lines across the banners and briefing cards, each one a manual repair of this problem at a single site.

**No channel between DOM trees.** Banners mount into CodeMirror widget decorations and markdown post-processor output; the briefing lives in a workspace leaf. The `updateRequest` event bubbles composed, but a composed event still only reaches ancestors within its own tree. A banner edit cannot reach the briefing by construction, and the event's only handler responds by refetching the entire briefing for a single changed field.

**Core-originated change is invisible.** The vault watcher syncs markdown into the database, the state-policy file sync service writes the database back out to markdown, and the CLI and scheduler write directly. None of it reaches the frontend. The vault is a legitimate second author and the UI cannot see it.

**Denormalization.** The API returns nested graphs — an objective carries its directive and its onrush sprint, a polaris cycle carries executives that carry objectives, the briefing carries the current onrush and polaris whole. The same entity arrives as many unrelated instances through many endpoints. Even a perfect event channel would not help, because nothing in the frontend knows that a change to one objective dirties the briefing and the cycle that contains it. This is why the existing workaround degrades to refetching everything, and it is the reason the other three cannot be fixed piecemeal.

# The Identity Map
## Entity Identity
Every entity carries a PUCK in `id`, and every serialized object already carries `@type` — the core stamps the runtime type name onto every object in every response, nested ones included, ordered first. `ModelValue` exposes the corresponding registry of type names to constructors.

An entity's identity in the frontend is therefore `{@type}:{id}`, and it is derivable from any payload without per-endpoint knowledge. This is what makes a generic repository system possible at all: no normalization schema has to be written or maintained per contract.

## Canonical Instances
The identity map holds exactly one instance per identity. When a response arrives carrying an entity already in the map, its fields are **merged into the existing instance** rather than replacing it.

Merging rather than replacing is deliberate. Every component that holds a reference to that instance is, by definition, already looking at the current state — cross-surface synchronization reduces to notifying components that they should re-render, not to threading new object references through the tree. Templates that read `entity.directive.title` keep working untouched, and the class instances that `ModelValue` reconstructs stay intact.

## Graph Absorption
Every response is walked before it reaches a caller. Each node identifiable as an entity is absorbed into the map, and its position in the parent graph is rewritten to point at the canonical instance. Absorption is recursive: after a briefing is absorbed, the objective nested three levels inside its polaris cycle *is* the same instance a banner elsewhere is displaying.

Absorption is the only entry point for API data into the frontend. Nothing bypasses it.

# Repositories
A repository per domain — objectives, directives, declaratives, onrush, polaris, lore — plus a system repository for non-entity aggregates. Repositories present the surface the SDK presents today, so adoption is mechanical rather than a rewrite.

## Read Path
Reads are served from the identity map when fresh and fetched when not. Concurrent reads of the same identity share one in-flight request; today an editor banner and a reading-mode banner for the same note issue two identical GETs, and every list that embeds an entity refetches what another surface already has.

Freshness is a per-repository policy, not a global constant. Aggregates like the briefing are cheap to consider stale; individual entities are not.

## Write Path
Mutations go through the repository, which applies the change optimistically, sends it, reconciles the response into the map, and rolls back on failure.

Rollback is not a refinement. `ObjectiveBanner` currently discards the failure result of `update` and leaves the edited value on screen, so a rejected write is indistinguishable from an accepted one. A write path that owns the optimistic apply owns the rollback too, and the failure surfaces once, in one place, instead of at every call site.

## Aggregates
Not everything the API returns is an entity. The briefing, watcher issue reports and note resolutions are views over entities. Repositories treat these as **derived records**: cached under their own key, absorbed for the entities they contain, and invalidated by the entities they depend on rather than by a timer alone.

# Subscription
## Entity References
Components do not fetch. They declare an **entity reference** — a reactive controller bound to an identity, which resolves it, subscribes to it, re-renders its host on change, and releases the subscription when the host disconnects.

This is where the repository system becomes visible to the UI. `EntityBanner.initialized`, every manual refetch line, and the `updateRequest` event all collapse into it.

## Loading & Error
Because resolution is owned by the reference rather than by each component's ad-hoc `await`, loading and failure become states the reference exposes and components render, instead of the current implicit "the template renders empty until the field is assigned". This subsumes the standing task for a global loading mechanism and unified entity components.

# Invalidation
## Dependencies
A change to an entity dirties more than that entity. Repositories declare these relationships once, as data: an objective change dirties the briefing, its onrush sprint, the polaris cycle holding it, and its directive's rollups.

The alternative — every mutation site knowing what else to refresh — is the state the frontend is in now, and it is why the briefing is refetched wholesale on any change that manages to reach it.

## Coalescing
Invalidations settle before they are acted on. A burst of edits, or one write that dirties four aggregates, produces one refresh pass rather than one per invalidation.

# The Change Feed
## Publication
Core publishes entity changes on a feed. The publication point is an EF `SaveChanges` interceptor, following the precedent `PlaintorchStatePolicyInterceptor` already sets: centralizing on the save hook is what makes every write pathway — API, watcher sync, CLI, scheduler — share the behaviour. Publishing per endpoint would catch only the writes the frontend already knows about, which are the ones that need it least.

Each event carries the identity and the kind of change. Payloads are not shipped; the frontend already knows how to resolve an identity, and shipping identities keeps the feed independent of contract shape.

## Transport
The feed is exposed as server-sent events. The transport abstraction gains a streaming operation alongside its request/response one, implemented over `EventSource` for the loopback listener and over a streaming `node:http` request for the unix socket.

Server-sent events rather than SignalR: the channel is one-directional because writes already travel over REST, Kestrel is configured HTTP/1.1-only on both listeners so a websocket upgrade over the unix socket is the fragile path, and the client side is a parser rather than a dependency.

## Echo Suppression
A write made through a repository has already been reconciled into the map by the time its own event returns on the feed. Events attributable to a local mutation are dropped rather than triggering a redundant resolve.

The feed also interacts with `VaultWatcherWriteBarrier`: an API write that causes a markdown write that the watcher re-reads must not produce a second round of events. The barrier already suppresses service-originated filesystem events for this reason, and the feed is expected to inherit that suppression rather than reimplement it.

## Degradation
The feed is an optimization, never a correctness requirement. If it cannot be established or drops, the store falls back to freshness policy and revalidation on surface activation. Every guarantee in this PEP except promptness holds with the feed switched off entirely.

# Placement
The repository system lives in the SDK, not in the Obsidian plugin. Identity, absorption, caching, invalidation and the feed client are frontend-agnostic and belong next to the contracts they consume. Only the entity reference controller is Lit-specific, and it is a thin binding over a store that does not know what Lit is.

The plugin becomes one consumer of the system rather than the place it is implemented.

# Consequences
The system replaces, rather than supplements:
- the per-component entity copies and the manual refetch lines that maintain them;
- the `updateRequest` event and the full-briefing refetch it triggers;
- the bespoke note-resolution cache in the system SDK, which is a single-purpose instance of what the store does generally.

It closes two standing tasks — using a push channel to update the briefing, and adding a global loading mechanism with unified entity components — and it is a precondition for any surface that displays the same entity in more than one place at once.

# Patches

## Patch106.1 - Tracked References, Forks & Queries
The repository system shipped and works, but a sweep of the surfaces built on it shows the same three manual steps leaking back into call sites — the exact hand-repair this PEP set out to delete, displaced one level up. This patch closes that gap by making the reference a first-class *tracked* object in the sense an ORM means it: one that observes itself, propagates its own edits, can be forked for isolated work, and can be filtered into a live collection. It draws its boundary explicitly against the concurrent core-side REFACTOR Alpha, which owns the `{@type}:{id}` contract this rests on.

### What diverged
Three steps meant to be automatic are being done by hand, each a place the guarantee broke and was patched locally.

**Watching is manual.** Absorption merges into the canonical instance *in place*, so its object reference never changes and Lit's `===` property check never fires. Every non-banner surface must therefore hand-wire an `EntityWatch` or it silently stops re-rendering. Only three files do; `BriefingCardOnrush` is missing it and is deaf to cross-surface edits today — the identical bug the polaris card was just patched for, one file over. The banner family avoided this by putting the reference in its base class, which is the tell: the fix is to make the reference universal, not to remember it per component.

**In-place edits do not propagate themselves.** A two-way binding writes straight into the canonical instance, leaving nothing for absorption to detect, so the edit is invisible elsewhere unless the surface also calls `touch()`/`publish()` by hand. Writing and broadcasting are two separate manual acts.

**Cross-entity refresh is hand-rolled.** After a `mutate()` that was supposed to invalidate dependents, surfaces run `Promise.all([…refresh, …revalidate])` themselves — because a write that *creates* a relationship (an objective joining a cycle) makes an edge the client's instance does not yet carry, so the local dependency table cannot find the other end, and because echo suppression drops the write's own feed events. The dependency graph the system was built to own is re-specified at every call site.

The rest of this patch introduces one primitive per divergence, plus the propagation change that retires the third.

### The tracked Reference
The reference (`EntityRef` today) becomes the single object a surface holds and the single path through which it edits. Three properties make it *tracked*:

- **It observes itself.** On connect it subscribes to its identity; on the store announcing a change it re-renders its host; on disconnect it releases. No component adds an `EntityWatch`, and none can forget to. A surface *handed* an instance rather than resolving one (a list row) establishes the same subscription from the instance's identity. `subscribeAll` is no longer the only way to "react to this entity," so surfaces stop reaching for the hammer.
- **It owns the write.** The reference integrates with the `@a11d/lit` `Binder` — a `Ref`-aware variant of the `ReactiveBinder` already in use. The binder already fires once, on the editable's commit event, after the editable's own validation, and its `sourceUpdate`/`sourceUpdated` hooks are exactly the two moments the reference needs. So `${ref.bind('status')}` wires the whole cycle: snapshot for rollback (before), write-through into the canonical instance (the optimistic edit), broadcast, send, and rollback on rejection. The `beginEntityEdit`/`commitEntityEdit`/`publishEntityEdit` trio hand-assembled in the banner base collapses into the reference, and the redundant component-level `get/set entity` dance goes with it. (For now `commit` still delegates the send, rollback and invalidation to the repository's imperative `mutate`; consolidating that split is Forking's job — see below.)
- **Its send strategy is per field, declared once.** The one genuinely bespoke part of a write is *which* endpoint persists a given field — a status change is a workflow shift, a title change is an update. That stays explicit (it is domain, per REFACTOR Alpha's principle 5), declared as a persist map on the reference rather than branched inside every `sourceUpdated`.

### Forking
Inline single-field editing is immediate: the binder writes through and propagates on the spot. Everything else — a multi-field modal with a cancel, a background recomputation — wants a boundary. A **fork** provides it: `ref.fork()` returns a draft only the forking surface sees, mutated with plain assignment; `commit()` sends the changes and reconciles them into the shared store; `cancel()` discards them.

Change tracking is by **snapshot and diff**, not by proxy. The fork captures the entity's field values at fork time; commit compares the draft against that baseline and sends only what changed. This is deliberately the mechanism a mainstream TypeScript ORM (MikroORM) uses — it keeps a copy of every property on load and diffs on flush, detecting changes *"rather than property interception."* An interception layer (a `Proxy` masquerading as the entity) was considered and rejected: it answers the same "what changed" question by the opposite, push means, and once a fork diffs on commit there is nothing left for it to do — its only unique capability, making an *un-forked* imperative write propagate instantly, is a behaviour this patch does not want (per-assignment sends, nested-mutation leaks, `===` confusion).

Two properties the diff must hold:
- **The baseline is the fork-time snapshot, not the live instance.** A concurrent edit to a *different* field of the canonical instance must survive the commit; diffing against the live instance would re-assert unchanged fields and clobber it. Diffing against the fork baseline sends exactly the fields this fork touched — the pull side of an optimistic-concurrency check, keyed on the store revision captured at fork.
- **The copy preserves the class.** A draft made by spreading loses the prototype and its computed getters. The fork copies through the same `ModelValueConstructor` path construction already uses (`new Constructor` + writable-guarded field copy), so a draft is a real instance of its class. Forks are **shallow and field-level** by policy: collections are edited through explicit API actions, never by mutating a draft's array (which, shared by reference, would reach the live instance) — matching the existing shallow `snapshot`.

Immediate editing and forking are the two editing modes this system needs, made concrete: a reference edits immediately by default; a fork is opted into where atomicity or cancellation matters.

**Forking folds the write cycle into one owner.** The tracked reference's `commit` delegates the send, rollback, and invalidation to the repository's imperative `mutate` — deliberately, to avoid re-implementing them — but that leaves the cycle split: the reference captures the snapshot and broadcasts, while `mutate` marks the ordering, rolls back, and invalidates, so a single field edit marks a local change three times over. `mutate` also predates the store's revision ordering, which now makes its in-flight `mutating` guard an *optimisation* rather than a correctness requirement — a stale refetch that races a write is discarded on arrival regardless of the guard. Forking changes `commit`'s contract from *run this operation* to *diff a draft against its baseline and send a patch*, which `mutate`'s operation-shaped signature fits poorly. So this is where the reference takes the whole cycle — talking to `store.snapshot`/`restore`/`noteLocalChange`/`invalidate` directly — and `mutate` demotes to a thin helper for the imperative, non-binding call sites that still call it directly (canvas actions, add-to-Polaris, the checkpoint toll). Its `invalidate` stays reachable regardless: it is the path that propagates a write with the feed off.

### Queries
A surface that shows *many* entities under a condition — active objectives, a directive's children — has today only two tools: a server-computed listing (a `DerivedRepository`, not reactive to local edits) or `subscribeAll` + filter in the component (reactive but O(all entities) per change). Neither is a live query.

A **query reference** is the missing primitive: a predicate over a base set that recomputes membership when a member changes and re-renders its host — the client analogue of `context.Objectives.Where(o => o.active)` under change tracking. It is affordable only once two costs are paid, which this patch pays as prerequisites:
- **Announcements are batched.** Today every changed absorb/touch notifies every `subscribeAll` subscriber synchronously, so an inline edit rebuilds a structural view once per keystroke. Global announcements coalesce on a microtask (the mechanism `InvalidationScheduler` already uses), collapsing a burst into one notification.
- **The store is indexed by type.** A query over objectives should hear about objectives, not about every entity. A per-type subscription lets a query subscribe to its kind rather than to the whole store.

With those in place a query reference is cheap, and the grid and canvas stop using `subscribeAll` as a structural hammer.

### Propagation without hand-rolling
The hand-rolled fan-out after a write — `Promise.all([…refresh, …revalidateObserved, …revalidateIfObserved])` at roughly eleven sites — was read as a workaround for echo suppression swallowing the write's own events. On inspection it is not. Echo suppression is only the per-key in-flight `mutating` guard, which holds the *written* identity and never its owners; and the change feed, started at load, already announces the affected owners structurally (`OwnersOf`) and lets them through. So the fan-out is **redundant**, not compensating for a suppressed signal: a write's own `mutate` already refreshes the written entity and revalidates the observed records, and the feed already invalidates the owners the local dependency table cannot reach — the edge a relationship-creating write just made is not yet in the client's instance, but the core sees it.

The cleanup is therefore a *removal*, taken conservatively. The strictly-redundant refetches go — re-`refresh`ing the entity `mutate` just invalidated, re-`revalidate`ing the briefing that `mutate`'s records pass already covers. The one that is *not* redundant stays: a relationship **owner** — the active-cycle banner after "Add to Polaris" — is reached only by the feed, and dropping its manual `revalidateObserved` would make its promptness hostage to the feed being up. That trade — an instant owner refresh versus feed-down degradation to the next activation tick — is kept as belt-and-suspenders until the feed's reliability is something we choose to lean on wholesale.

Either way this inherits the feed's one known-ambiguous edge: the `Directive`/`LunarDirective` owner announcement noted in `core/.DISCUSSION.md`, harmless while no child record carries a directive foreign key. REFACTOR Alpha's family descriptor is where that edge is resolved rather than worked around; this patch tracks it and does not depend on it.

### Grounding — what this reuses and assumes
- **`@a11d/api-dotnet` is the substrate, and the fork reuses it.** The `@type` convention the identity map keys on, and the reconstruction of real class instances (getters, methods, `instanceof`), both come from `ModelValueConstructor`. The fork's prototype-preserving copy *is* that same construction, not a new mechanism.
- **Identify and construct must not drift.** Recognising an entity is structural (`@type` + string `id` + string `title`); constructing one is gated on `@model` registration. A type identified but not registered is tracked as a prototype-less plain object — the failure mode that once dropped every directive. This patch adds a startup assertion that every structurally-tracked type is `@model`-registered, so the two cannot diverge. It is the client-side complement to the core's `EntityTypeNameContractTests`; a concrete type-name change stays a two-repository contract change.
- **The identity map is unbounded, and the bound is active forgetting.** MikroORM's warning that a shared identity map keeps *"every entity that became managed"* is our long-session future: the store never evicts, and `changedAt` never prunes. Lacking their fork-per-request-and-clear escape — we are one long-lived context — the store must forget, in two parts of unequal difficulty. **The change ledger** prunes on a low-water mark of in-flight reads: a `changedAt` marker discards only responses issued *before* the change it records, so once no read still in flight predates it, it is spent and dropped — idle clears the ledger outright. The tempting shortcut, dropping the marker as soon as any newer read lands, is unsafe: a slower *older* read, issued before the edit, would then revert it on arrival. **Entity eviction** — the subtler half — is a reachability sweep gated by the canonical-instance guarantee. Derived caches hold their entities' canonical instances for the life of the cache, so eviction cannot go by subscribers alone: `PlaintorchRepositories.sweep()` retains every entity a resolved derived view still holds (listings, briefing) or a subscription still reaches, and the store drops the rest. A reachable record is never evicted — a later fetch would otherwise mint a divergent second instance — so only entities held nowhere are freed: one fetched for a since-closed banner, or dropped from a refreshed listing.

### Coordination with REFACTOR Alpha
This patch is client/SDK-side; REFACTOR Alpha is core-side, and the two meet only at the SDK contract surface, which the refactor touches rarely. The boundaries held here:
- **Write payloads are not genericised.** The per-field persist map dispatches through the *existing* per-entity update/shift contracts. A uniform patch endpoint is out of scope, deferred to align with REFACTOR Alpha's phase 5 API kit and consistent with its principle 5 (per-entity payload shapes are domain, not glue).
- **Type names stay a shared contract.** The `@model`-registration assertion reinforces — does not fork — the `{@type}:{id}` contract gated by `EntityTypeNameContractTests`.
- **Invalidation consumes the feed as-is.** Feed-scoped echo suppression is a client change; it relies on owner announcements the interceptor already emits, adds no core surface, and tracks (does not resolve) the directive/lunar owner edge that is the family descriptor's to close.

### Housekeeping
Carried in the same pass because they are the seams these primitives sit on:
- **One registry table.** Adding an entity type touches three parallel lists in `repositories.ts` (constructor, `byTypeName`, `records`) — the drift that once dropped directives, most recently exercised by `ExecutiveOrder`. Drive all three from one declarative `{typeName, fetcher}` table.
- **One cache coordinator.** `EntityRepository` and `DerivedRepository` re-implement the same in-flight dedup, freshness window, and subscriber plumbing; a shared base removes the duplication, value-in-store vs value-local being the only difference.
- **Batched announcements** (above), plus two correctness nits noticed in passing: `InvalidationScheduler.settled()` can resolve before a flush begins, and overlapping flushes are possible under a burst.

### Rollout
Each step lands independently and leaves the system working.
1. **The tracked Reference** — auto-watch, binder-owned write, and per-field persist map in the shared base; retire the hand-wired `EntityWatch`/`publish` and the `get/set entity` boilerplate. Fold in feed-scoped echo suppression and the `@model`-registration assertion.
2. **Batched, type-indexed store** — the perf prerequisites; retire `subscribeAll` as a structural hammer.
3. **Forking** — `fork()`/`commit()`/`cancel()` with diff-against-baseline; migrate multi-field modals and any background recomputation onto it.
4. **Query references** — filtered live collections over the indexed store.

Housekeeping (registry table, cache base, eviction/prune) rides alongside whichever step it unblocks.

### Consequences
The patch replaces, rather than supplements:
- the per-component `EntityWatch` wiring and the manual `touch()`/`publish()` calls;
- the hand-rolled `Promise.all` refresh fan-out after writes;
- `subscribeAll` as the mechanism for list-shaped and filtered views;
- the ad-hoc snapshot/rollback dance in the banner base.

It makes the reference behave the way this PEP's opening promised the *entity* would — edited anywhere, current everywhere — with forking and filtering as first-class operations rather than things a surface assembles by hand.
