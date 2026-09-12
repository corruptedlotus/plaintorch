# Criteria — Client SDK & Repository System

A living catalogue of what the TypeScript client SDK (`sdk.ts/`) — above all the frontend **repository system** of [[PEP106 - Frontend Repository System]] — **must** guarantee, what is currently **tested**, and what remains open. It is both a specification of invariants and the backlog for ongoing coverage. The core has its own catalogue in [[Criteria - Validators, Checks & Unit Test]].

**Status legend:** ✅ tested · ⏳ planned/pending · 🔲 not yet planned
**Kind:** _happy_ (nominal), _edge_ (boundary/unusual input), _fault_ (must fail safely / reject)

Tests live beside their subject as `plaintorch/**/*.test.ts` and run under **Vitest** (`npm test` in `sdk.ts`, i.e. `vitest run`). The reactive core is pure and in-memory, so a suite needs no core process, socket, or DOM — a real `EntityStore` and the fakes in `plaintorch/repository/test-utils.ts` (`makeEntity`, `flushMicrotasks`, `countingFetcher`, `recordingInvalidationTarget`) are the whole kit.

> **Toolchain note.** Vite 8's default transform is oxc, which does not apply the SDK's legacy `@model` registration decorators. `vitest.config.ts` therefore runs the project's own TypeScript through esbuild first (a `pre` plugin using `transformWithEsbuild`, which honours `experimentalDecorators`). `transformWithEsbuild` is deprecated in Vite 8; the long-term alternative is to convert the six `@model('X')` decorators to post-class `model('X')(Class)` calls (the form `directives/models.ts` already uses for its second registration), which every transformer handles natively and which would retire both the esbuild devDependency and the plugin.

---

## Identity — `identity.test.ts`
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

## Absorption — `absorption.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| A list response becomes constructed instances, not plain objects | happy | ✅ | `absorption.test.ts` (shallow-construct regression) |
| A nested entity is the same instance as a directly fetched one | happy | ✅ | `absorption.test.ts` (identity map) |
| Non-entity nodes are returned untouched | edge | ✅ | `absorption.test.ts` |
| A root's empty array applies (a genuine emptying) | edge | ✅ | `absorption.test.ts` |
| A nested empty array is withheld (no Include back-reference wipe) | fault | ✅ | `absorption.test.ts` (wipe regression) |
| A nested `[null]`-holed array is withheld (IgnoreCycles hole) | fault | ✅ | `absorption.test.ts` |
| A nested entity still merges its populated arrays and scalars | happy | ✅ | `absorption.test.ts` |

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

## Invalidation — `invalidation.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Dependents resolved against the canonical instance, not the payload | happy | ✅ | `invalidation.test.ts` |
| `settled()` waits for the flush, not resolving before it begins | fault | ✅ | `invalidation.test.ts` (nit regression) |
| A burst coalesces into one pass | happy | ✅ | `invalidation.test.ts` |
| Work queued mid-flush drains on the same chain — no concurrent flush | fault | ✅ | `invalidation.test.ts` (nit regression) |
| Relationship-creating writes cannot discover the new owner locally | edge | ⏳ | — (documented gap; feed-driven) |

## Repository contract / drift guard — `contract.test.ts`
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `PlaintorchRepositories` constructs — every routed type is `@model`-registered | fault | ✅ | `contract.test.ts` (mirrors core `EntityTypeNameContractTests`) |
| Each routed type constructs to a class instance, never a plain object | fault | ✅ | `contract.test.ts` |
| `forTypeName` routes every type; undefined for unknown / undefined | happy | ✅ | `contract.test.ts` |

---

## Tier 2 — repositories, drafts, feed, client (planned)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `EntityRepository.get` serves fresh; refetches stale/expired; `force` bypass | happy/edge | ⏳ | — |
| Two concurrent `get(id)` share one fetch (in-flight dedup) | edge | ⏳ | — |
| `mutate` rolls back on reject, invalidates on success | happy/fault | ⏳ | — |
| `commit` (store-direct) returns a boolean; `fork` undefined when unresolved | happy/edge | ⏳ | — |
| `revalidateIfObserved` skips when unobserved **and** when writing | edge | ⏳ | — |
| `EntityDraft`: prototype-correct copy; isolation before commit | happy | ⏳ | — |
| `EntityDraft.diff` is only the fields changed from the fork baseline | happy | ⏳ | — |
| `commit` applies + invalidates; rolls back on reject; concurrency survival | happy/fault | ⏳ | — |
| `cancel`; spent-after-commit; empty-diff no-op; shallow caveat | edge | ⏳ | — |
| `DerivedRepository.commit` notifies only when the value actually changed | edge | ⏳ | — |
| `DerivedRepository` freshness / in-flight dedup | happy | ⏳ | — |
| `ChangeFeed` `toLines` reassembly (split line, multi-line chunk, CRLF, partial) | edge | ⏳ | — |
| `ChangeFeed.dispatch`: critical → acceptAuthority + invalidate; malformed ignored | happy/fault | ⏳ | — |
| `ChangeFeed` reconnect backoff (doubling, capped, reset on good connection) | edge | ⏳ | — |
| `coreClient` captures `issuedAt` before the request; reviver absorbs the response | happy | ⏳ | — |
| `sendForSuccess` and the non-ok path consume the body | fault | ⏳ | — (socket-pin regression) |

## Tier 3 — transport integration (planned; real in-process socket/pipe)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| A body-returning boolean write does not pin its keep-alive connection | fault | ⏳ | — (socket-pin regression) |
| An unanswered request fails within the timeout, never hangs; identity recovers | fault | ⏳ | — (timeout / dedup-poisoning regression) |
| A timed-out GET retries once; a POST/PUT/DELETE does not | edge | ⏳ | — |
| `stream()` bounds only the connect; the feed never occupies a pool slot | edge | ⏳ | — |

## Lit controllers (plugin package — separate Vitest setup needed)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `EntityRef` subscribes on connect, re-renders on change, releases on disconnect | happy | 🔲 | — |
| `EntityRef.binder` runs the snapshot → send → rollback cycle on a fake host | happy/fault | 🔲 | — |
| `EntityWatch` observes a handed instance; `publish` broadcasts an in-place edit | happy | 🔲 | — |
| `QueryRef.items` filters + orders the live type; re-renders on a member change | happy | 🔲 | — |

---

## Known / possible faults to keep covered
- **Structural drift** — a concrete `@type` the core emits that no repository recognises or routes; guarded by `identity.test.ts` + `contract.test.ts`, and cross-checked against the core's `EntityTypeNameContractTests`.
- **Value-object false-tracking** — a reference carrying another entity's id (an endpoint) must never be tracked as an entity; guarded by `identity.test.ts`.
- **Sparse-payload erasure** — a response that omits a field must not overwrite the cached value with a default; guarded by `entityStore.test.ts`.
- **Include back-reference wipe** — a nested, unloaded collection (empty or `[null]`-holed) must not blank a collection a direct fetch populated; guarded by `absorption.test.ts`.
- **Sync reverting an edit** — a response issued before a local change must be discarded, not applied; guarded by `entityStore.test.ts` (supersede ordering).
- **Socket pinning** — an undrained response body over the keep-alive pool exhausts it and looks like the core hanging; to be guarded by the Tier-3 transport suite.
- **Hung request / dedup poisoning** — an unanswered request must settle as a bounded failure rather than poisoning an identity's in-flight entry forever; Tier-3.
- **Invalidation settling / overlap** — `settled()` must not resolve before the flush, and flushes must not overlap; guarded by `invalidation.test.ts`.
- **Unbounded growth** — MikroORM's identity-map warning, one level down. `changedAt` now prunes on a low-water mark of in-flight reads — a marker is spent once no read predates it — bounding the ledger without ever reverting an edit a slower read could clobber; guarded by `prune.test.ts`. Store-*entity* eviction (dropping records with no subscribers) remains the outstanding Patch106.1 groundwork: the subtle half, gated by the canonical-instance guarantee.
