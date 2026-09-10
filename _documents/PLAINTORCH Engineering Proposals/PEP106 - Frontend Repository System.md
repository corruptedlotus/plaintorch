---
status: implemented
assignee: Copilot 🤖
phase: 2A
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
