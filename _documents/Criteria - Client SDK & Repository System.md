# Criteria — Client SDK & Repository System

A living catalogue of what the TypeScript client SDK (`sdk.ts/`) — above all the frontend **repository system** of [[PEP106 - Frontend Repository System]] — **must** guarantee, what is currently **tested**, and what remains open. It is both a specification of invariants and the backlog for ongoing coverage. The core has its own catalogue in [[Criteria - Validators, Checks & Unit Test]].

**Status legend:** ✅ tested · ⏳ planned/pending · 🔲 not yet planned
**Kind:** _happy_ (nominal), _edge_ (boundary/unusual input), _fault_ (must fail safely / reject)

Tests live beside their subject as `*.test.ts`. The generic repository system is the package `@corruptedlotus/local-repo` in its own repository beside this one (`npm test` there); the suites about what PLAINTORCH declares to it (its identifier, its merge rules, its dependents, its repositories, its client) are under `sdk.ts/plaintorch/repos/` and `sdk.ts/plaintorch/` (`npm test` in `sdk.ts`). All run under **Vitest**. The reactive core is pure and in-memory, so a suite needs no core process, socket, or DOM — a real `EntityStore` and the fakes in the package's `testing.ts` (`@corruptedlotus/local-repo/testing`: `makeEntity`, `flushMicrotasks`, `countingFetcher`, `recordingInvalidationTarget`) are the whole kit. The package's Lit bindings have a suite of their own under happy-dom (`local-repo/lit/bindings.test.ts`), and SIPA's wrappers theirs (`npm test` in `sipa`).

> **Toolchain note.** Vite 8's default transform is oxc, which does not apply the SDK's legacy `@model` registration decorators. `vitest.config.ts` therefore runs the project's own TypeScript through esbuild first (a `pre` plugin using `transformWithEsbuild`, which honours `experimentalDecorators`). `transformWithEsbuild` is deprecated in Vite 8; the long-term alternative is to convert the six `@model('X')` decorators to post-class `model('X')(Class)` calls (the form `directives/models.ts` already uses for its second registration), which every transformer handles natively and which would retire both the esbuild devDependency and the plugin.

---

## Policy — `policy.test.ts`
The package's files assume no domain. A store is built with an `EntityPolicy` (identity, model construction, mergeable keys), and these run it over a domain that shares nothing with PLAINTORCH's to prove each is the policy's to decide. Suites about the store's mechanics use the small `testPolicy` in `testing.ts`; suites about what PLAINTORCH tells the store (identity, absorption, navigation properties, eviction, the contract) use `plaintorchEntityPolicy`.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `identifyByShape` recognises a type name + string id under configured fields | happy | ✅ | `policy.test.ts` |
| `identifyByShape` rejects empty / missing / non-string type or id, and non-objects | fault | ✅ | `policy.test.ts` |
| `identifyByShape` applies a further `requires` test | edge | ✅ | `policy.test.ts` |
| `resolvePolicy` defaults to plain models and every-key merging | happy | ✅ | `policy.test.ts` |
| A foreign-shaped entity (no `@type`, no title) is tracked, one instance across responses | happy | ✅ | `policy.test.ts` |
| A value the policy does not recognise is not tracked, whatever another policy would say | fault | ✅ | `policy.test.ts` |
| Equivalence, the reachability walk and the sweep all follow the store's identifier | happy | ✅ | `policy.test.ts` |
| A payload absorbs as an instance of the class the policy constructs | happy | ✅ | `policy.test.ts` |
| A draft of a type with no model class is still an isolated copy, prototype kept | fault | ✅ | `policy.test.ts` (alias-the-canonical guard) |
| Default merge trusts every key; a policy withholds keys and is told which value is the root | happy/edge | ✅ | `policy.test.ts` |
| The package's core imports nothing from outside it; its Lit bindings nothing but `@a11d/lit` (suites may also import `vitest` and `node:`) | fault | ✅ | `policy.test.ts` (separation guard) |
| Every relative import in the package carries its `.js` extension, so the built JavaScript resolves under Node as emitted | fault | ✅ | `policy.test.ts` |

## Identity — `identity.test.ts`
PLAINTORCH's identifier (`repos/policy.ts`), built on `identifyByShape`; its suite is `repos/identity.test.ts`. The key helpers (`entityKey`, `typeNameFromKey`) stay in the package's `identity.test.ts`.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `entityKey` / `typeNameFromKey` round-trip | happy | ✅ | `identity.test.ts` |
| `typeNameFromKey` splits on the first colon; whole string when none | edge | ✅ | `identity.test.ts` |
| `typeNameOf` reads `@type`; undefined for non-objects / missing | happy/edge | ✅ | `identity.test.ts` |
| An entity is `@type` + non-empty string `id` + string `title` | happy | ✅ | `identity.test.ts` |
| Recognises whatever concrete `@type` the core emits, not a fixed list | fault | ✅ | `identity.test.ts` (StellarDirective drift) |
| A value object that references an entity but has no title is **not** tracked | fault | ✅ | `identity.test.ts` (EndpointRef) |
| A child record keyed on a number is **not** tracked | fault | ✅ | `identity.test.ts` |
| Rejects empty id / missing-or-non-string title / no `@type` / non-object | fault | ✅ | `identity.test.ts` |
| Works on a constructed instance (`@type` survives construction) | edge | ✅ | `identity.test.ts` |

## Equivalence — `equivalence.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Deep-compares primitives, plain objects, arrays | happy | ✅ | `equivalence.test.ts` |
| `null` / `undefined` handling | edge | ✅ | `equivalence.test.ts` |
| Entities short-circuit on identity, not field-by-field | happy | ✅ | `equivalence.test.ts` |
| An entity and a non-entity are never equivalent | edge | ✅ | `equivalence.test.ts` |
| Terminates when a cycle runs through an entity reference | edge | ✅ | `equivalence.test.ts` |
| Gives up past the depth cap (deep plain structure compares unequal) | edge | ✅ | `equivalence.test.ts` (documented boundary) |

## Entity store — `entityStore.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Absorb-new adopts; `peek`/`has` resolve it | happy | ✅ | `entityStore.test.ts` |
| Merge touches only payload keys — a sparse response never erases cached fields | fault | ✅ | `entityStore.test.ts` (sparse-merge regression) |
| The canonical instance reference stays stable across merges | happy | ✅ | `entityStore.test.ts` |
| Identical re-absorb bumps no version and notifies nobody | edge | ✅ | `entityStore.test.ts` |
| A non-entity is returned untouched | edge | ✅ | `entityStore.test.ts` |
| A response issued before a local change is discarded | fault | ✅ | `entityStore.test.ts` (supersede-ordering regression) |
| An authoritative response applies even over a local change | edge | ✅ | `entityStore.test.ts` |
| `acceptAuthority` gives up the local claim so the next read wins | edge | ✅ | `entityStore.test.ts` |
| Without `issuedAt`, a response is never superseded | edge | ✅ | `entityStore.test.ts` |
| Per-identity subscriber fires synchronously on an in-place edit | happy | ✅ | `entityStore.test.ts` |
| Subscribe-before-absorb (placeholder), then fire on first absorb | edge | ✅ | `entityStore.test.ts` |
| Releasing a subscription stops notifications | happy | ✅ | `entityStore.test.ts` |
| A burst coalesces into one store-wide notification | happy | ✅ | `entityStore.test.ts` |
| A type subscriber fires for its own type only | happy | ✅ | `entityStore.test.ts` |
| `entitiesOfType` enumerates a type's members | happy | ✅ | `entityStore.test.ts` |
| Nothing is scheduled when no structural observer exists | edge | ✅ | `entityStore.test.ts` |
| `patch` merges and notifies; `restore` reverts | happy | ✅ | `entityStore.test.ts` |
| `snapshot` is shallow — nested collections are shared | edge | ✅ | `entityStore.test.ts` (documented limitation) |
| `beginWrite`/`endWrite`/`isWriting` guard + both-ends revision note | happy | ✅ | `entityStore.test.ts` |
| `markStale` / `markAllStale` / unknown-reads-stale | happy/edge | ✅ | `entityStore.test.ts` |

## Absorption — `repos/absorption.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| A list response becomes constructed instances, not plain objects | happy | ✅ | `absorption.test.ts` (shallow-construct regression) |
| A nested entity is the same instance as a directly fetched one | happy | ✅ | `absorption.test.ts` (identity map) |
| Non-entity nodes are returned untouched | edge | ✅ | `absorption.test.ts` |
| A root's empty array applies (a genuine emptying) | edge | ✅ | `absorption.test.ts` |
| A nested empty array is withheld (no Include back-reference wipe) | fault | ✅ | `absorption.test.ts` (wipe regression) |
| A nested `[null]`-holed array is withheld (IgnoreCycles hole) | fault | ✅ | `absorption.test.ts` |
| A nested entity still merges its populated arrays and scalars | happy | ✅ | `absorption.test.ts` |

## Navigation properties — `repos/navigationProperties.test.ts`
A null single-reference navigation (`directive`, `parentDirective`) is withheld unless the same payload also nulls its `<field>Id` — an id still set beside a null nav is incoherent, the signature of a nav that was not loaded rather than genuinely cleared. Closes the field-trial regressions where an objective update erased its directive and a directive banner lost its parent (while the tree, reading the scalar id, held).

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| A lean update carrying `directive:null` (id still set) keeps the populated directive | fault | ✅ | `navigationProperties.test.ts` (nav-erasure regression) |
| A summary `get` carrying `parentDirective:null` (id still set) keeps the parent | fault | ✅ | `navigationProperties.test.ts` (nav-erasure regression) |
| A response that omits the nav key keeps it (omitted keys never merge) | edge | ✅ | `navigationProperties.test.ts` |
| A nested null single-reference is withheld like an empty collection | fault | ✅ | `navigationProperties.test.ts` |
| A genuine clear (FK and nav both null) still propagates | happy | ✅ | `navigationProperties.test.ts` |
| Eviction retains a nav record via the closure and never nulls a field | edge | ✅ | `navigationProperties.test.ts` (rules out the eviction hypothesis) |

## Write cycle — `mutation.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `isSuccessfulMutation`: undefined/null/false fail; 0/''/object/true succeed | happy/edge | ✅ | `mutation.test.ts` |
| `runWrite` holds the write guard for the operation, releases after | happy | ✅ | `mutation.test.ts` |
| Invalidates on success; does not roll back | happy | ✅ | `mutation.test.ts` |
| Rolls back a rejected write; does not invalidate | fault | ✅ | `mutation.test.ts` |
| Rolls back **and rethrows** on a thrown operation, guard still released | fault | ✅ | `mutation.test.ts` |
| Honours a custom success predicate | edge | ✅ | `mutation.test.ts` |

## Change-ledger prune — `prune.test.ts`
The `changedAt` ledger grows one marker per locally edited identity. A marker at revision _r_ only ever discards a response _issued before r_, so once no in-flight read predates it, it can never drop a response again and is pruned. `beginRead`/`endRead` bracket every response-absorbing request; the oldest in-flight read is the prune cutoff (the low-water mark). The overriding invariant is that pruning must **never** strand an edit a slower read could clobber.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `beginRead` stamps a read with the current revision (parity with the old capture) | happy | ✅ | `prune.test.ts` |
| Concurrent reads at one revision are a multiset — each released independently | edge | ✅ | `prune.test.ts` |
| `endRead` for a revision never opened is a no-op | fault | ✅ | `prune.test.ts` |
| The ledger clears once the last read settles with nothing else in flight | happy | ✅ | `prune.test.ts` |
| Prune is read-driven — a change lingers until a read settles, then is swept | edge | ✅ | `prune.test.ts` |
| Only markers at or below the oldest in-flight read are pruned (low-water mark) | happy | ✅ | `prune.test.ts` |
| A marker is held while an older read is in flight, so its stale response is dropped | fault | ✅ | `prune.test.ts` (revert-guard) |
| Held until the **last** reader at the oldest revision settles (multiset is load-bearing) | fault | ✅ | `prune.test.ts` (revert-guard) |
| Once pruned, a genuinely newer read applies normally | edge | ✅ | `prune.test.ts` |
| A read taken mid-write is discarded by the end-of-write marker, then pruned | fault | ✅ | `prune.test.ts` |
| Overlapping reads — newer wins, older dropped, neither reverts the edit | happy/fault | ✅ | `prune.test.ts` |
| A sparse merge under an in-flight read is unaffected by the prune | edge | ✅ | `prune.test.ts` |
| E2e — get, edit + commit, a concurrent stale list dropped, then the ledger empties | happy | ✅ | `prune.test.ts` |
| E2e — a throwing read still deregisters and prunes (the client's `finally`) | fault | ✅ | `prune.test.ts` |

## Entity eviction — `eviction.test.ts`
The identity map is bounded by a reachability sweep. `collectEntityKeys` walks a value graph for the entities it holds; `PlaintorchRepositories.sweep()` gathers what every resolved derived view reaches and hands it to `EntityStore.sweep`, which keeps the observed, the reachable, and anything mid-write, and drops the rest. The overriding invariant is the canonical-instance guarantee: an entity a live view still holds must never be evicted, or a later fetch mints a divergent second instance.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `collectEntityKeys` collects a flat entity, and descends arrays + nested objects | happy | ✅ | `eviction.test.ts` |
| `collectEntityKeys` terminates on an entity-reference cycle | edge | ✅ | `eviction.test.ts` |
| `collectEntityKeys` ignores non-entities (no-title value objects, numeric keys, primitives) | fault | ✅ | `eviction.test.ts` |
| Evicts an unsubscribed entity nothing holds — record, type-index, and ledger entry all gone | happy | ✅ | `eviction.test.ts` |
| Keeps a subscribed entity even when no view holds it | happy | ✅ | `eviction.test.ts` |
| Keeps an entity a live view reaches (external root) | happy | ✅ | `eviction.test.ts` |
| Keeps the entities a subscribed entity nests (closure over subscribed roots) | fault | ✅ | `eviction.test.ts` (canonical-instance guard) |
| Keeps an entity with a write in flight | edge | ✅ | `eviction.test.ts` |
| Keeps a subscribed placeholder; no choke on its undefined value | edge | ✅ | `eviction.test.ts` |
| A held entity stays the same instance; an unheld one evicts and refetches fresh | fault | ✅ | `eviction.test.ts` (canonical-instance guard) |
| `RepositorySet.sweep` keeps what any resolved record holds, evicts the rest — every derived repository is a root | happy | ✅ | `repositorySet.test.ts` |
| An unresolved or emptied record pins nothing | edge | ✅ | `repositorySet.test.ts` |
| `PlaintorchRepositories.sweep` keeps what a resolved listing holds, evicts the rest | happy | ✅ | `repos/eviction.test.ts` |
| `PlaintorchRepositories.sweep` keeps a subscribed entity absent from every listing | edge | ✅ | `repos/eviction.test.ts` |
| Periodic sweep runs each interval; not before the first; stop halts it; start is idempotent | happy/edge | ✅ | `eviction.test.ts` |
| A periodic tick actually evicts an unheld entity | happy | ✅ | `eviction.test.ts` |

## Invalidation — `invalidation.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Dependents resolved against the canonical instance, not the payload | happy | ✅ | `invalidation.test.ts` |
| A type the table does not name, or no table, dirties only the entity and the records | edge | ✅ | `invalidation.test.ts` |
| An entity the store does not hold has no relations to follow | edge | ✅ | `invalidation.test.ts` |
| PLAINTORCH's table: objective → directive, sprint, parent, cycles; decree, fate, directive, sprint ↔ order, lore | happy | ✅ | `repositoryDependents.test.ts` |
| PLAINTORCH's table leaves an absent relation undefined rather than naming an empty identity | edge | ✅ | `repositoryDependents.test.ts` |
| `settled()` waits for the flush, not resolving before it begins | fault | ✅ | `invalidation.test.ts` (nit regression) |
| A burst coalesces into one pass | happy | ✅ | `invalidation.test.ts` |
| Work queued mid-flush drains on the same chain — no concurrent flush | fault | ✅ | `invalidation.test.ts` (nit regression) |
| Relationship-creating writes cannot discover the new owner locally | edge | ⏳ | — (documented gap; feed-driven) |

## Repository set — `repositorySet.test.ts`
`RepositorySet` is where a repository becomes known to everything that spans repositories. `PlaintorchRepositories` extends it and only declares which repositories exist.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `entity` routes its type name; `forTypeName` is undefined for unknown / undefined | happy | ✅ | `repositorySet.test.ts` |
| `entity` refuses a type the store's policy has no model for, naming it | fault | ✅ | `repositorySet.test.ts` |
| A write through a set's repository revalidates the observed dependents the table names | happy | ✅ | `repositorySet.test.ts` |
| A repository's own options (freshness) pass through | edge | ✅ | `repositorySet.test.ts` |
| `revalidateEntityIfObserved` routes by key, splitting on the first colon only | edge | ✅ | `repositorySet.test.ts` |
| A write revalidates observed records and leaves the rest stale | happy | ✅ | `repositorySet.test.ts` |
| `invalidateAll` marks every entity and record stale without fetching | happy | ✅ | `repositorySet.test.ts` |
| `revalidateObserved` refetches observed entities and records together | happy | ✅ | `repositorySet.test.ts` |
| `acceptAuthority` gives up the local claim so an older response applies | edge | ✅ | `repositorySet.test.ts` |
| As a change feed's sink: an event refetches an observed entity, a critical one over a local edit | happy | ✅ | `repositorySet.test.ts` |
| Periodic sweep: each interval, not before the first, idempotent start, safe stop, a tick evicts | happy/edge | ✅ | `repositorySet.test.ts` |

## Read bracketing — `read.test.ts`
`EntityStore.read` is the one call a client wraps around a request whose response is absorbed; `PlaintorchCoreClient` runs every body-returning request through it (`coreClient.test.ts`).

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| In flight for exactly the duration of the request | happy | ✅ | `read.test.ts` |
| The parser absorbs to canonical instances, list responses element by element | happy | ✅ | `read.test.ts` |
| A response for an identity changed while in flight is discarded | fault | ✅ | `read.test.ts` |
| An authoritative read applies over such a change | edge | ✅ | `read.test.ts` |
| The read closes when the request or the parse throws | fault | ✅ | `read.test.ts` |
| The ledger prunes when the read settles, but not while an older read is in flight | fault | ✅ | `read.test.ts` |

## Data states & read outcomes — `states.test.ts`
A fetcher answers with a value, with an authoritative nothing (`undefined`), or with `unresolved` when no trustworthy answer came back. The store keeps one of three data states per identity — `not-loaded`, `loaded`, `removed`. The overriding invariants: an outage must never read as emptiness or as a deletion, and a removed entity must never be brought back by a response older than its removal.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| An identity is `not-loaded` until absorbed, whether unheard of or merely observed | happy | ✅ | `states.test.ts` |
| `remove` drops the instance, marks an observed identity `removed`, bumps its version and notifies | happy | ✅ | `states.test.ts` |
| An observed identity that never loaded can be removed (the backend says it does not exist) | edge | ✅ | `states.test.ts` |
| An unobserved identity leaves no record behind; removing twice or the unknown is a no-op | edge | ✅ | `states.test.ts` |
| A removal notifies the type's observers, so a live collection drops the member | happy | ✅ | `states.test.ts` |
| A removed identity absorbed again is `loaded`, as a new instance | happy | ✅ | `states.test.ts` |
| A response issued before the removal does not bring the entity back; one issued after does | fault | ✅ | `states.test.ts` (resurrection guard) |
| `snapshot` / `patch` / `touch` / `restore` are inert on a removed identity | edge | ✅ | `states.test.ts` |
| A removed identity is swept once nothing observes it | edge | ✅ | `states.test.ts` |
| `wasRemoved(instance)` is true for the instance canonical at removal, observed or not, and after the identity is absorbed again | fault | ✅ | `states.test.ts` (late-mount / single-reference guard) |
| `wasRemoved` answers for a single reference detaching left in place; false for anything not a removed entity | edge | ✅ | `states.test.ts` |
| Freshness stamps live on the record: only a held identity is stamped; evicted with it; cleared by removal | fault | ✅ | `states.test.ts` (stamp-leak regression) |
| `unresolved` keeps a held entity and is not counted as a fresh resolution | fault | ✅ | `states.test.ts` (failed-read-as-fresh regression) |
| `unresolved` for a never-loaded identity leaves it `not-loaded` | edge | ✅ | `states.test.ts` |
| An answer of nothing removes a held entity; marks an observed never-loaded one `removed` | happy | ✅ | `states.test.ts` |
| A removed identity is asked for again and comes back when the backend has it | edge | ✅ | `states.test.ts` |
| An answer of nothing does not remove an entity absorbed or changed while the request was in flight | fault | ✅ | `states.test.ts` (older-answer guard) |
| `revalidateObserved` refetches what the repository resolved, not what only arrived in a listing | edge | ✅ | `states.test.ts` |
| An invalidated entity stays eligible for `revalidateObserved` | edge | ✅ | `states.test.ts` |
| Write success is the repository's to declare: default rejects an absent result on every write path | fault | ✅ | `states.test.ts` |
| A repository (or a whole set) that accepts absent results accepts them on `mutate`, `commit` and a draft's `commit` | happy | ✅ | `states.test.ts` (204-backend regression) |
| A single `mutate` can override the repository's test | edge | ✅ | `states.test.ts` |
| Derived: `unresolved` keeps the last value, notifies nobody, leaves the record stale | fault | ✅ | `states.test.ts` (outage-blanks-view regression) |
| Derived: an answer of nothing empties the record and notifies | happy | ✅ | `states.test.ts` |
| Derived: `isResolved` tells never resolved from resolved to nothing; an unanswered record recovers | edge | ✅ | `states.test.ts` |
| Derived `prune` drops unobserved records that are expired, stale or never resolved; spares the in-flight | happy/edge | ✅ | `states.test.ts` |
| A set's sweep prunes first, so an expired unobserved view no longer pins its entities; an observed one always does | fault | ✅ | `states.test.ts` |
| **The foreign-domain fixture** — a bookshop with its own identity (per-type id fields, no title, no `@type`), merge policy, dependents and a 204-answering backend runs read, write, outage, feed, removal and eviction through the generic pieces alone | happy/fault | ✅ | `foreignDomain.test.ts` (acceptance) |
| `ChangeFeed`: a `Deleted` event removes at once, after queueing its invalidation; other operations remove nothing | happy | ✅ | `changeFeed.test.ts` |

## Removal reach — `detach.test.ts`
What still holds an entity once it is removed is the store's policy: `"detach"` (the default, and PLAINTORCH's) takes it out of every collection the store can reach; `"keep"` touches nothing else. The overriding invariant under detach is that an observer told of the removal already finds the entity gone from everything it reads.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `detachFrom` returns a new array without the instance; the same value, unchanged, when nothing held it | happy/edge | ✅ | `detach.test.ts` |
| Every occurrence is taken out; a collection that did not hold it keeps its reference | edge | ✅ | `detach.test.ts` |
| A collection is replaced on its owner at any depth short of another entity; the walk stops at other entities | happy/edge | ✅ | `detach.test.ts` |
| A single reference is left pointing at the instance | edge | ✅ | `detach.test.ts` (documented boundary) |
| Terminates on a cycle; does not write to a frozen holder | fault | ✅ | `detach.test.ts` |
| A removal takes the entity out of an owner's collection as a new array, bumps and notifies the owner, leaves it stale | happy | ✅ | `detach.test.ts` |
| An entity that did not hold it is not notified, not replaced, not stale | edge | ✅ | `detach.test.ts` |
| An observer of the removed entity already finds it gone from its owners when told | fault | ✅ | `detach.test.ts` |
| The owners' type observers are told along with the removed entity's | happy | ✅ | `detach.test.ts` |
| Under `"keep"` nothing but the identity itself is touched; the mode can be given per type; the default is detach | happy/edge | ✅ | `detach.test.ts` |
| A listing loses the row at once, as a new array, and notifies — without a refetch | happy | ✅ | `detach.test.ts` |
| A record that did not hold it is untouched; a nested collection in a view loses it; every key lets go | edge | ✅ | `detach.test.ts` |
| A record that *was* the entity resolves to nothing and is left stale | edge | ✅ | `detach.test.ts` |
| PLAINTORCH: a listing loses the row, a sprint loses the objective, the current-sprint record empties — none refetched | happy | ✅ | `repos/outcomes.test.ts` |

## PLAINTORCH outcomes — `repos/outcomes.test.ts`
The same guarantees through the real client over a scripted transport (`repos/test-utils.ts`), so the path is the application's: transport, client, reviver, store.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `getJsonAnswer`: a success (empty included) and a not-found (404 / 410) are answers | happy | ✅ | `repos/outcomes.test.ts` |
| `getJsonAnswer`: silence, a 5xx and any other refusal are not; `getJson` keeps its return-nothing contract | fault | ✅ | `repos/outcomes.test.ts` |
| A listing keeps what it holds through silence and a 500, and asks again next time | fault | ✅ | `repos/outcomes.test.ts` |
| A listing empties on an empty answer; is not loaded, rather than empty, when never answered | happy/edge | ✅ | `repos/outcomes.test.ts` |
| A record with nothing to name (no current sprint) resolves to nothing rather than staying unloaded | edge | ✅ | `repos/outcomes.test.ts` |
| A note resolution keeps its last answer through an outage | fault | ✅ | `repos/outcomes.test.ts` |
| An entity is kept when the core does not answer or is failing (500 / 503 / 409), and is not stamped fresh | fault | ✅ | `repos/outcomes.test.ts` |
| An entity is removed when the core answers that it does not exist; an observed unknown PUCK reads `removed` | happy | ✅ | `repos/outcomes.test.ts` |
| A deletion announced on the feed removes the entity at once and refreshes the listing that held it | happy | ✅ | `repos/outcomes.test.ts` |
| An entity first seen as a back-reference is adopted without the cycle hole; a partly truncated collection keeps its members | fault | ✅ | `repos/outcomes.test.ts` (hole-reaches-consumer regression) |
| A truncated collection still never replaces one a fuller response loaded | fault | ✅ | `repos/outcomes.test.ts` |
| Over the fetch transport a body is read exactly once; a refusal and a boolean write drain without failing | fault | ✅ | `repos/outcomes.test.ts` (double-read regression) |

## Repository contract / drift guard — `repos/contract.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `PlaintorchRepositories` constructs — every routed type is `@model`-registered | fault | ✅ | `contract.test.ts` (mirrors core `EntityTypeNameContractTests`) |
| Each routed type constructs to a class instance, never a plain object | fault | ✅ | `contract.test.ts` |
| `forTypeName` routes every type; undefined for unknown / undefined | happy | ✅ | `contract.test.ts` |

---

## Tier 2 — repositories, drafts, feed, client (the rest of `EntityDraft` still planned)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `EntityRepository.get` serves fresh; refetches stale/expired; `force` bypass | happy/edge | ✅ | `entityRepository.test.ts` |
| A refetch that changed a field keeps the same instance | happy | ✅ | `entityRepository.test.ts` |
| Two concurrent `get(id)` share one fetch (in-flight dedup); a forced read joins it | edge | ✅ | `entityRepository.test.ts` |
| A failed fetch does not poison the identity — the next read issues a fresh request | fault | ✅ | `entityRepository.test.ts` (dedup-poisoning guard) |
| `mutate` holds the identity, rolls back on reject or throw, invalidates on success | happy/fault | ✅ | `entityRepository.test.ts` |
| A successful `mutate` counts as a fresh resolution | edge | ✅ | `entityRepository.test.ts` |
| `commit` (store-direct) returns a boolean; `fork` undefined when unresolved | happy/edge | ✅ | `entityRepository.test.ts` |
| `revalidateIfObserved` skips when unobserved **and** when writing | edge | ✅ | `entityRepository.test.ts` |
| `revalidateObserved` refetches only resolved, observed identities | edge | ✅ | `entityRepository.test.ts` |
| `EntityDraft`: prototype-correct copy; isolation before commit | happy | ✅ | `policy.test.ts`, `draft.test.ts` |
| `EntityDraft.diff` is only the fields changed from the fork baseline | happy | ✅ | `entityRepository.test.ts` |
| `commit` applies + invalidates; rolls back on reject; concurrency survival | happy/fault | ⏳ | — |
| `cancel`; spent-after-commit; empty-diff no-op; shallow caveat | edge | ⏳ | — |
| `DerivedRepository.commit` notifies only when the value actually changed | edge | ✅ | `derivedRepository.test.ts` |
| A listing of the same canonical members in a fresh array notifies nobody; a membership change does | edge | ✅ | `derivedRepository.test.ts` |
| `DerivedRepository` freshness / in-flight dedup / failed fetch not poisoning the key | happy/fault | ✅ | `derivedRepository.test.ts` |
| `DerivedRepository` `invalidate` (one / all), `revalidateIfObserved`, `revalidateObserved`, `resolvedValues` | happy/edge | ✅ | `derivedRepository.test.ts` |
| `toLines` reassembly (split line, multi-line chunk, CRLF, trailing partial) | edge | ✅ | `internal/transport.test.ts` |
| `ChangeFeed.dispatch`: critical → acceptAuthority + invalidate; malformed, comment and incomplete events ignored | happy/fault | ✅ | `changeFeed.test.ts` |
| `ChangeFeed` resynchronises on every (re)connect; an absent or throwing source is not an error | happy/fault | ✅ | `changeFeed.test.ts` |
| `ChangeFeed` `start` is idempotent; `stop` aborts the connection and ends reconnecting | edge | ✅ | `changeFeed.test.ts` |
| `ChangeFeed` reconnect backoff (doubling, capped, reset on good connection) | edge | ✅ | `changeFeed.test.ts` |
| `coreClient` opens the read before the request leaves; the response absorbs to the canonical instance | happy | ✅ | `coreClient.test.ts` |
| `coreClient` discards a response superseded in flight; an authoritative read applies; the read closes on nothing / empty / refusal / throw | fault | ✅ | `coreClient.test.ts` |
| A boolean write is not a read and consumes its body | fault | ✅ | `coreClient.test.ts` |
| The non-ok path consumes the body | fault | ✅ | `repos/outcomes.test.ts` (socket-pin regression) |

## Tier 3 — transport integration (planned; real in-process socket/pipe)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| A body-returning boolean write does not pin its keep-alive connection | fault | ⏳ | — (socket-pin regression) |
| An unanswered request fails within the timeout, never hangs; identity recovers | fault | ⏳ | — (timeout / dedup-poisoning regression) |
| A timed-out GET retries once; a POST/PUT/DELETE does not | edge | ⏳ | — |
| `stream()` bounds only the connect; the feed never occupies a pool slot | edge | ⏳ | — |

## Lit bindings — the package's `lit/bindings.test.ts`
The package's own suite: real `@a11d/lit` hosts under happy-dom over a bookshop store, with a native `<input>` driving the binder. Under Node, Lit resolves to its server build, so the suite installs the default binding property of native inputs itself.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `EntityRef` resolves and renders; re-renders on an edit made elsewhere; follows a changed identifier | happy/edge | ✅ | `lit/bindings.test.ts` |
| Two hosts of one identity share one instance and one request | happy | ✅ | `lit/bindings.test.ts` |
| `EntityRef` reads removed (backend says gone) and not-loaded (backend unreachable), with no error either time | fault | ✅ | `lit/bindings.test.ts` |
| `EntityRef` releases its subscription when the host leaves the document | happy | ✅ | `lit/bindings.test.ts` |
| `EntityRef.binder` through a real `<input>`: write-through, broadcast to another host, the `'*'` fallback sends only the changed field, the reaction runs, the write revalidates the observed entity | happy | ✅ | `lit/bindings.test.ts` |
| `EntityRef.binder` rejected: rolled back on every surface, the input restored, the `rejected` hook called — no built-in notice | fault | ✅ | `lit/bindings.test.ts` |
| `EntityWatch` re-renders on `publish` without fetching; `removed` and `entity-removed` after `store.remove` | happy/fault | ✅ | `lit/bindings.test.ts` |
| `QueryRef` is a live filtered + ordered slice; re-renders when a member enters the predicate | happy | ✅ | `lit/bindings.test.ts` |
| `DerivedRef` resolves, re-renders on a refetch; not-loaded while the backend cannot be asked | happy/fault | ✅ | `lit/bindings.test.ts` |

## SIPA's bindings — `sipa/components/data/bindings.test.ts`
SIPA's wrappers (the toast on a rejected write, the installed core's store) over a real PLAINTORCH client. SIPA has its own Vitest setup (`npm test` in `sipa`; `vitest.config.ts` is the legacy-decorator transform plus `happy-dom`). The bindings are mounted as real elements over a real client whose core is a scripted table.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `EntityRef` resolves and renders; re-renders on a change made elsewhere; releases on disconnect | happy | ✅ | `bindings.test.ts` |
| `EntityRef` follows a change of identifier while mounted; two hosts share one request | edge | ✅ | `bindings.test.ts` |
| `EntityRef` reads not-loaded (core unreachable) and removed (core says gone), re-rendering to say so | fault | ✅ | `bindings.test.ts` |
| `EntityRef.commit` broadcasts an accepted edit; rolls back a rejected one with a toast | happy/fault | ✅ | `bindings.test.ts` |
| `EntityRef.fork` yields a draft once resolved, none before | edge | ✅ | `bindings.test.ts` |
| `EntityRef.binder` runs the snapshot → send → rollback cycle through a real element | happy/fault | ✅ | the package's `lit/bindings.test.ts` (a native `<input>`; a SIPA editable in the harness stays 🔲) |
| `EntityWatch` re-renders a host handed an entity; `publish` broadcasts an in-place edit | happy | ✅ | `bindings.test.ts` |
| `EntityWatch.removed` and the `entity-removed` attribute, also for a host mounted after the removal | fault | ✅ | `bindings.test.ts` |
| `QueryRef.items` filters + orders the live type; re-renders on a member change, a new member, a removal | happy | ✅ | `bindings.test.ts` |
| `DerivedRef` resolves and re-renders on record change; loses a removed member without a refetch; reads not-loaded | happy/fault | ✅ | `bindings.test.ts` |

---

## Known / possible faults to keep covered
- **Structural drift** — a concrete `@type` the core emits that no repository recognises or routes; guarded by `identity.test.ts` + `contract.test.ts`, and cross-checked against the core's `EntityTypeNameContractTests`.
- **Value-object false-tracking** — a reference carrying another entity's id (an endpoint) must never be tracked as an entity; guarded by `identity.test.ts`.
- **Sparse-payload erasure** — a response that omits a field must not overwrite the cached value with a default; guarded by `entityStore.test.ts`.
- **Include back-reference wipe** — a nested, unloaded collection (empty or `[null]`-holed) must not blank a collection a direct fetch populated; guarded by `absorption.test.ts`.
- **Unloaded single-reference erasure** — a null single-reference nav (id still set) from a lean write or a summary must not blank a nav a listing populated; the FK's coherence tells an unloaded nav from a genuine clear; guarded by `navigationProperties.test.ts`.
- **Sync reverting an edit** — a response issued before a local change must be discarded, not applied; guarded by `entityStore.test.ts` (supersede ordering).
- **Socket pinning** — an undrained response body over the keep-alive pool exhausts it and looks like the core hanging; to be guarded by the Tier-3 transport suite.
- **Hung request / dedup poisoning** — an unanswered request must settle as a bounded failure rather than poisoning an identity's in-flight entry forever; Tier-3.
- **Invalidation settling / overlap** — `settled()` must not resolve before the flush, and flushes must not overlap; guarded by `invalidation.test.ts`.
- **Outage read as emptiness** — a request the core did not answer must leave what is held in place, still stale, rather than blank a listing or count as a fresh read; guarded by `states.test.ts` and `repos/outcomes.test.ts`.
- **Deleted entity still shown** — an entity the core says does not exist must leave the store and tell its observers, never linger as the last instance fetched; guarded by `states.test.ts` and `repos/outcomes.test.ts`.
- **Outage read as deletion** — the reverse: only an authoritative not-found removes, never silence or a failing core; guarded by `repos/outcomes.test.ts`.
- **Resurrection by a stale response** — a listing issued before a removal must not bring the entity back; guarded by `states.test.ts`.
- **Cycle hole reaching a consumer** — a collection truncated by `IgnoreCycles` must not be adopted with its `null` in it; guarded by `repos/outcomes.test.ts`.- **Unbounded growth** — MikroORM's identity-map warning, one level down. `changedAt` prunes on a low-water mark of in-flight reads — a marker is spent once no read predates it — bounding the ledger without ever reverting an edit a slower read could clobber; guarded by `prune.test.ts`. Store-*entity* eviction is a reachability sweep (`PlaintorchRepositories.sweep`) that drops only records neither observed nor reachable from a live view, respecting the canonical-instance guarantee; guarded by `eviction.test.ts`. It runs on a host-started periodic interval this version; three other triggers (idle-detected, on-refresh, memory-threshold) are recorded in PEP106 for a later pass, and further gains would need bounding the derived caches themselves.
