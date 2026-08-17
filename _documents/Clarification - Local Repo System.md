> This blueprint describes the frontend repository system introduced in [[PEP106 - Frontend Repository System]]. It lives in the SDK (`sdk.ts/plaintorch/repository`) and the Obsidian plugin, and is entirely separate from the core-side `PlaintorchRepository`, which is an unrelated data-access service that happens to share the word.

# Abstract
The local repository system is the frontend's single source of truth for entities. Before it, every component fetched and held its own copy of an entity, so two surfaces showing the same thing were unrelated objects and an edit to one was invisible to the other. The repository system replaces that with one canonical instance per entity identity, shared by reference across every surface. Keeping surfaces in step then reduces to telling them to re-render — no data is ever copied between them.

Around that single idea sit the supporting concerns: recognising what is an entity, caching reads and de-duplicating them, applying writes optimistically and rolling them back on failure, propagating changes the frontend did not make (the watcher, the CLI, the scheduler), and ordering all of the above so a slow response can never revert a fresh edit.

# In Brief
The whole system is one rule and three flows.

**The rule.** There is exactly one object per entity identity, and every surface holds a reference to *that* object. When a response arrives it is *absorbed*: an entity already known has the incoming fields merged into the instance already on screen, rather than replacing it. Synchronising surfaces is then only a matter of notifying their subscriptions.

**Reading.** A surface asks a repository for an entity by id. The repository serves it from the store if it is fresh, de-duplicates concurrent requests for the same identity into one, and otherwise fetches. Whatever comes back is absorbed and the canonical instance is returned.

**Writing.** Edits are applied to the canonical instance first (optimistically), then sent. On success the write invalidates whatever it made stale; on failure the instance is rolled back to a snapshot taken before the edit and the user is told.

**Staying current.** Writes made outside the frontend arrive over a change feed and are turned into invalidations. When the feed drops and reconnects, everything is marked stale and whatever is on screen is refetched.

# The Store
The store (`EntityStore`) holds exactly one record per identity: the canonical instance, a version counter, a staleness flag, and the set of subscribers observing it.

## Identity
An entity is recognised by *shape*, not by a hard-coded list of type names. The core stamps a runtime type name (`@type`) onto every serialised object, and the `IPuckNamedEntity` contract is a PUCK token plus a title. So a value is a tracked entity exactly when it carries a type name, a non-empty **string** `id`, and a **string** `title`. Its store identity is then `{typeName}:{id}`.

Both halves of the shape test carry weight. The `id` must be a string because child records such as a dependency edge key on a number and must not be tracked. The `title` must be present because a value object can carry a string `id` that is a *reference* to another entity — a dependency endpoint does — and tracking those would merge two unrelated references that happen to point at the same entity into one instance.

Recognising an entity is therefore structural and universal; only *routing* a type name to a specific repository is declared by hand. This is deliberate: an earlier closed list of type names drifted out of step with the core (the concrete stellar directive serialises as `StellarDirective`, the list said `Directive`), and every directive silently stopped being tracked, which is invisible because an entity that is never absorbed simply never notifies anyone.

## Absorption
Absorption is what makes a response populate the store. Responses are parsed with a reviver that runs bottom-up, so by the time a parent is revived its children are already constructed and already canonical. This is what makes *nested* models and *list* responses become real, tracked instances — constructing only the root, as the client did before this system, left nested models as plain objects and skipped list responses entirely, since a bare array carries no type name of its own.

For each revived value:
- If it is not a tracked entity, it is returned untouched.
- If its identity is unseen, the instance is adopted as canonical.
- If its identity is known, the incoming fields are **merged into** the existing instance rather than replacing it, and subscribers are notified **only if a field actually changed**. A poll that returns identical data re-renders nothing, and unchanged arrays and objects keep their references across a refetch.

Merging is restricted to the keys the wire payload actually carried. Constructing a model widens a sparse response to the full shape of its class — declared fields appear as `undefined`, initialised ones as their defaults — so without this restriction a response that merely omits a field would overwrite the cached value with a default.

A further distinction is drawn between the **root** of a response and a **nested** entity reached through it. The root speaks fully for the entity it fetched, empty collections included — a directly fetched sprint with no checkpoints is a genuine emptying. A nested entity is usually a back-reference dragged in by an `Include` whose own navigations were not loaded, so the core serialises an unloaded collection as an empty array and a cycle back-reference as a hole; a nested entity therefore withholds its empty and holed arrays while merging its scalar and single-reference fields, so it cannot wipe a collection a direct fetch had populated.

## Subscriptions
A surface may subscribe to an identity before it has ever been fetched; the store keeps a placeholder record so the subscription is live from the start and the first absorption adopts the incoming instance verbatim.

There are two kinds of subscription:
- **Per-identity**, for a surface bound to one entity — it re-renders when that instance changes.
- **Store-wide**, for a surface whose very shape depends on entity fields rather than on one entity. A tree built from parent references restructures when any member is reparented, which no per-identity subscription would report, because the entity that moved is still the same entity.

## In-place edits
A two-way binding writes an edit straight through to the canonical instance before anything is sent, so the change is already applied by the time absorption would run and there is nothing left for it to detect. Such an edit is announced to the other surfaces by explicitly *touching* the identity, which bumps its version and notifies subscribers.

# Reading and Writing
## Repositories
An `EntityRepository` coordinates reads and writes of one entity type against the shared store. The store owns identity; the repository owns *when the core is actually contacted*. Its responsibilities:
- **A freshness window.** A resolved entity is served from the store without contacting the core until it goes stale or its window elapses.
- **In-flight de-duplication**, keyed by identity. Two banners for the same note — one in the editor, one in reading mode — issue one fetch, not two.
- **`mutate()` as the single write path.** It applies the write, and on rejection rolls the instance back to a caller-supplied snapshot (the caller is the only one who can capture the previous state, because a two-way binding has already changed the instance by the time the write is sent). On success it invalidates whatever the change made stale. While a write is in flight the identity is *held*, so no revalidation — including the echo of this very write arriving on the feed — can refetch over an edit still being made.

## Derived records
Not everything a surface shows is an entity. The briefing, a note-to-entity resolution, a PUCK resolution, and the various listings are *views* over entities with no identity of their own. A `DerivedRepository` caches these under a key of the caller's choosing, with the same freshness and de-duplication machinery. The entities *inside* a derived record are still absorbed on the way through the client, so a listing and a surface showing one of its members individually agree without either knowing about the other; what the record adds is membership.

## Invalidation
A write marks stale not only the entity it changed but everything downstream of it. What is downstream of each type is declared once, in a dependency table, resolved against the **canonical** instance — the only place the answer is complete (an objective carries no cycle of its own, but its canonical instance carries the executives that name every cycle holding it). Declaring this once is the point; the alternative is every write site knowing what else to refresh.

Two properties keep this cheap:
- **Marking stale is not refetching.** Everything affected is always marked stale, but a refetch is only issued for an identity something is currently observing. Anything else is resolved lazily by whoever asks for it next; a briefing nobody is looking at is never fetched.
- **Coalescing.** Dirty identities collect in a set and flush on a microtask, so one write that touches four aggregates produces one revalidation pass rather than four.

# Staying Current
## The change feed
The change feed keeps the store in step with writes the frontend did not make. The core announces an entity change — a type, an id, and an operation — over a long-lived `text/event-stream`, and the feed turns each announcement into an invalidation.

The feed is strictly an optimisation: every guarantee of the system except *promptness* holds with it switched off, which is why a feed that cannot be established or that drops is never treated as an error. It simply leaves revalidation to the freshness window and to whatever the host wires up on surface activation. When a connection is (re-)established, anything that happened while disconnected was missed, so everything is marked stale and whatever is on screen is refetched.

## Ordering and consistency
Because reads, optimistic writes, and feed-driven refreshes race, the store needs an order. It keeps a monotonic **revision** counter that advances on every local change, and remembers the revision at which each identity last changed. A response records the revision the request was issued under, and is **discarded** for any identity that changed since — it was already out of date before it arrived. A counter rather than a clock, so two events in the same millisecond still order correctly.

The default is therefore *a local change always wins, and a sync is discarded rather than allowed to revert it*. The one exception is a change the core declares **critical** — the vault reconciling a hand-edited file, or a removal. There the frontend gives up its local claim on that identity, so the revalidation that follows is allowed to win: the vault is the authority on a file it just reconciled, and a removal leaves nothing for an edit to be about.

# Talking to the core
## Absorption at the client boundary
Absorption is wired at the **client**, not the repository. Every response the client reads — from a repository, a one-shot search, a picker, or a call site that has not moved onto repositories at all — flows through the absorbing reviver and contributes canonical instances to the store. Using a repository is what gets you caching and subscription; it is not what gets you canonical instances, which are universal.

## Connection model
The Obsidian plugin talks to the core exclusively over its per-user local socket — a named pipe on Windows, an `AF_UNIX` socket elsewhere. One-shot requests share a small, bounded **keep-alive connection pool**; reusing connections keeps a burst of revalidations from opening and tearing down a fresh pipe connection per request, and the pool's ceiling keeps a burst from opening an unbounded number at once — anything past the ceiling queues.

The long-lived change feed is deliberately kept **off** that pool, on its own dedicated connection. A stream held open for the whole session would otherwise occupy a pooled slot for its entire lifetime and starve the one-shot requests.

### Response-consumption invariant
Because the pool is keep-alive, a socket is not returned to it until its response body has been fully read. **Every one-shot response must therefore be consumed**, even when the caller only cares whether the request succeeded and ignores the body, and even when the response is an error. A body left undrained pins its connection; enough pinned connections exhaust the pool, at which point every subsequent request — reads included — queues with no socket to run on and fails on its timeout, until the server's own keep-alive timeout eventually closes the pinned connections. From the outside this is indistinguishable from the core hanging and then recovering, so the invariant is a correctness requirement of the transport, not an optimisation.

# Surfaces
Two small controllers connect a component to the store:
- **`EntityRef`** resolves an entity by id, subscribes, re-renders the host on change, and releases on disconnect. The id is read from a thunk on every host update, so a reference re-resolves when its target changes while the host stays mounted. Because every surface observing an identity is handed the same instance, an edit made anywhere reaches all of them without any of them knowing the others exist.
- **`EntityWatch`** is for a list item *handed* its entity as a property by an aggregate. That instance is already canonical, so the item only needs to learn when it changes — it fetches nothing.

A surface that asks for an entity by a type name no repository serves would otherwise render blank forever, which is how a drifted type name hides: the surface looks empty rather than broken. Such a lookup is warned about once per type name instead.
