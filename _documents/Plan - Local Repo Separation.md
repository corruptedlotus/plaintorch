> Draft plan for the "Separate Localrepo library" item of [[Phase 3A]]: turning the frontend repository system of [[PEP106 - Frontend Repository System]] into a library another project can adopt. It follows the readiness assessment of 2026-10-02 and is written against [[Clarification - Local Repo System]] and [[Criteria - Client SDK & Repository System]]. Nothing here is implemented yet.

# Goal
A project with its own backend and its own entity shapes can install one package, declare its entity policy and fetchers, and get the identity map, repositories, invalidation, change feed and Lit bindings — without importing anything PLAINTORCH. PLAINTORCH itself becomes the first consumer of that package and behaves exactly as it does today, except for the fixes in stage 3.

**Done means:** a fixture domain that is deliberately unlike PLAINTORCH (no `title`, no `@type` registry, writes answering nothing) runs the full read / write / feed / eviction cycle from the packed tarball, compiled with default TypeScript settings.

# Settled decisions
Ruled on 2026-10-02.

| Decision | Ruling |
|---|---|
| Package name and home | `@corruptedlotus/local-repo`, kept in this repository for now under `/local-repo`, consumed as `file:../local-repo` |
| Lit bindings | `@a11d/lit` is enforced: hosts are its `Component`, bindings extend its `Controller`, and it is a required peer of the bindings entry |
| Error system | Deferred. This plan does not wait for it; see *Reserved for the error system* for what is kept open |
| Removal and not-loaded | Both are states the store exposes. "Not loaded" is a state of its own, surfaced on a reference the way an error is; SIPA adapts its surfaces to render each |

# Ground rules
- **Stages 1, 2 and 5 are behaviour-neutral for PLAINTORCH.** Only stage 3 changes what a user can observe. Each stage lands on its own and leaves the Obsidian plugin and the standalone shell working.
- **Tests travel with their stage.** Before a stage moves a piece, it pins that piece's current behaviour; stage 4 is the remainder, not the first time tests appear. Stages 1 and 2 refactor `EntityRepository` and `DerivedRepository`, which have no suites today, so their characterisation tests are the first commit of stage 1.
- **The package stays at `0.x` while the error system is deferred.** It can be used by another project at `0.x`; what waits is the promise that its outcome shapes will not change.
- Verification after every stage: `npm test` and `npm run typecheck` in `sdk.ts`, the SIPA typecheck diffed against its baseline, the Obsidian esbuild bundle, and a manual pass over one banner edit, one listing and one feed-driven refresh.

# Stage 1 — Inject the policy
*Size: medium. Behaviour-neutral. Done 2026-10-02.*

Everything PLAINTORCH-specific inside the generic files becomes a value handed in at construction. The store takes one policy object; PLAINTORCH's is declared once, beside the client.

- [x] Characterisation suites for `EntityRepository` (fresh / stale / forced reads, in-flight de-duplication, `mutate`, `commit`, `fork`, `revalidateIfObserved`) and `DerivedRepository` (freshness, de-duplication, notify-only-on-change).
- [x] **Identity.** `identify` becomes a policy function the store owns (`store.identify`). `isEquivalent` and `collectEntityKeys` take it as a parameter instead of importing it. The library ships a preset factory for the common shape (a type key and an id key); PLAINTORCH supplies its own, which adds the string-`title` requirement. The free `identify` the SDK exports today stays, as PLAINTORCH's policy function, so SIPA imports do not change.
- [x] **Construction.** A small `ModelConstructor` interface (should-construct, construct, is-registered) replaces the direct use of `ModelValueConstructor` in `absorption.ts`, `draft.ts` and the registration guard. The `@a11d/api-dotnet` adapter lives in the SDK. The library default constructs nothing and forks with a prototype-preserving shallow copy.
- [x] **Merge policy.** `authoritativeKeys` becomes a policy hook: given a payload and whether it is the response root, which keys may merge. The library default trusts every key. The withheld-collection and unloaded-reference rules move to the SDK unchanged, as PLAINTORCH's (and any EF Core `IgnoreCycles` backend's) policy.
- [x] **Dependents.** The table in `invalidation.ts` moves to the SDK; `InvalidationScheduler` receives it.
- [x] Guard: a test asserting no file under `repository/` imports `@a11d/api*`.

# Stage 2 — Separate the composition
*Size: medium. Behaviour-neutral. Done 2026-10-02.*

The generic directory stops knowing that a PLAINTORCH client exists.

- [x] **Change feed.** A generic `ChangeFeed` over two small interfaces: a source (open a line stream) and a sink (invalidate one, invalidate all, revalidate observed, accept authority). The event parser is injectable; the default is today's JSON shape. `PlaintorchChangeFeed` becomes a thin declaration in the SDK carrying the `/api/system/changes` path.
- [x] **Repository set.** A generic base owning what `PlaintorchRepositories` does mechanically: type-name routing, `forTypeName`, the invalidation target, `sweep` and its timer, `invalidateAll`, `revalidateObserved`. Derived repositories are created through it, so each one is an eviction root automatically — this retires the "new listings must be added to `records`" gotcha. `PlaintorchRepositories` shrinks to its declarations.
- [x] **Read bracketing.** One store helper that opens a read, hands the caller an absorbing parser stamped with the right revision, and closes the read in a `finally`. The three hand-written copies in `coreClient.ts` collapse onto it, and an outside client has one call to get right instead of a protocol.
- [x] **Barrel split.** `repository/index.ts` exports only generic pieces. The PLAINTORCH composition (repositories, feed declaration, policy, dependents) and the suites about it move to a folder of their own, `plaintorch/repos/` — named for the client's `repos`; `plaintorch/index.ts` keeps exporting both, so no consumer import changes.
- [x] Guard: the stage-1 import test widens to "nothing under `repository/` imports from outside it".

# Stage 3 — Close the behavioural gaps
*Size: large. The only stage users can notice. Done 2026-10-02.*

Ordered so each fix rests on the one before it.

- [x] **A read has three outcomes, not two.** A fetcher may answer with a value, with an authoritative "nothing", or with an `unresolved` sentinel meaning no trustworthy answer came back. On `unresolved`, a derived record keeps its last value, stays stale and notifies nobody; an entity repository does not stamp freshness. This re-implements the intent of the unmerged `claude/local-repo-sync-clearing-e8540b` rather than merging it — that branch predates the write-cycle refactor and bypasses the domain SDKs with hard-coded paths.
- [x] **PLAINTORCH wiring for the above.** With the error system deferred, the client gets a small, isolated adapter that lets the composition's fetchers tell "unreached" from "empty". It is the piece the error system will eventually replace, so it stays out of the library and out of the domain SDKs' public surface.
- [x] **Three data states per identity, exposed by the store.** *Not loaded* (observed or asked for, never absorbed), *loaded*, and *removed*. Today the first is only an internal placeholder and the third does not exist. The same applies to a derived record: never resolved is distinct from resolved to nothing.
- [x] **Removal.** `store.remove(key)` moves an identity to *removed*: it clears the canonical value, notifies its subscribers and the structural ones, and *notes* the removal in the ledger — not drops the entry, as first planned — so a response issued before the removal cannot bring the entity back. An answer of nothing is ignored when the identity was absorbed or changed while that request was in flight. It fires on an authoritative "nothing" for a known identity and on a feed event whose operation is `Deleted` (parsed today, never used). A removed identity that is absorbed again becomes *loaded* with the incoming instance; holders of the old one learn of the removal from the notification and must let go of it.
- [x] **What held a removed entity lets go of it — configurably.** Added after review: most surfaces would rather lose the row than show one for something gone. The store's policy takes a `removal` mode, for every type or per type: `"detach"` (the default, and PLAINTORCH's) takes the entity out of the collections of the other entities the store holds and out of the derived records over it, replacing each collection and telling its observers; `"keep"` touches nothing else. Single references are left either way.
- [x] **References carry the states.** `EntityRef` exposes *not loaded* and *removed*, `DerivedRef` *not loaded*, and `EntityWatch` *removed*, beside `loading` and `error` — each its own readable state rather than something inferred from an undefined value. (A record has no identity to remove, and a watch is always handed a loaded instance.)
- [x] **SIPA adapts.** Ruled on review: a list item is simply *removed* (detaching takes it out of the listing that drew it), while banners and the modals switch to the removed panel (`p7t-entity-absence`). Banners do so through their reference, whether resolving by PUCK or shown a provided copy; the full panel stands down with its actions and editors; the executive and occurrence editors watch the entity they are about — the incentive, the owning declarative — through the embedded `EntityWatch` and switch to the panel when it is removed. A surface whose entity could not be loaded says so and offers to try again. The faded `entity-removed` state remains only as a fallback for what the store cannot detach.
- [x] **Cycle holes are repaired, not only withheld.** An entity first seen as a nested back-reference is adopted as it arrived, so a collection the core truncated with `IgnoreCycles` reaches consumers as `[null]` until a fuller response fills it. Withholding protects the merge only. (Found during stage 1; identical on the pre-separation code — the SDK's `.GENESIS.md` had described a repair that no longer exists.) No new hook was needed: PLAINTORCH's model constructor takes the `null` entries out of a payload's collections before constructing, so what is adopted is incomplete rather than unsafe to walk.
- [x] **Write success is policy.** The success predicate becomes a repository option, used by `mutate`, `commit` and `EntityDraft.commit` alike. PLAINTORCH keeps its "an absent result is a rejection" rule as its declared default; a backend that answers 204 declares otherwise.
- [x] **Bounded bookkeeping.** Freshness timestamps move onto the store's record, so they are evicted with it. The sweep also drops derived records that are both unobserved and expired.
- [x] **Client hygiene.** The debug log in `PlaintorchCoreClient.send` reads the response body before the caller does, which makes every read through the fetch transport reject. Read once, or drop the log.

# Stage 4 — Finish the test backlog
*Size: medium.*

- [ ] `EntityDraft`: prototype-correct copy, isolation before commit, diff against the fork baseline, rollback, spent-after-commit, the shallow caveat.
- [x] `ChangeFeed`: line reassembly, dispatch (critical, malformed), reconnect back-off under fake timers, reconnect marks everything stale. *Written with stage 2, before the feed was made generic; the `Deleted` operation is tested when stage 3 gives it meaning.*
- [x] Client wiring: revision captured before the request leaves, a boolean write's body consumed, a throwing read still closes its bracket. *Written with stage 2, before the bracketing moved into `store.read`. The non-ok path still does not consume its body — that stays open with the transport suite.*
- [ ] Lit bindings under a DOM test environment: `EntityRef` lifecycle and edit cycle, `EntityWatch`, `QueryRef`, `DerivedRef`, and each of the not-loaded / loading / loaded / removed / failed states.
- [ ] **The foreign-domain fixture** from *Goal* — the acceptance test for the whole effort, and the living example in the README.
- [ ] [[Criteria - Client SDK & Repository System]] updated: Tier 2 rows flipped, new rows for the data states, removal, read outcomes and policy injection.

The transport suite (Tier 3: socket pinning, timeouts over a real pipe) stays on the SDK's own backlog. It tests PLAINTORCH's transport, not the library, and does not gate this plan.

# Stage 5 — Package and bindings
*Size: large, mostly mechanical.*

- [ ] **Move.** `git mv` the generic directory to `/local-repo` as `@corruptedlotus/local-repo`, so history follows. No decorators, and no runtime dependencies in the core entry.
- [ ] **Entry points.** `.` for the core, which stays free of Lit so it runs and tests without a DOM; `./lit` for the bindings, with `@a11d/lit` as a required peer; `./testing` for the fakes in `test-utils.ts`, which outside consumers will want.
- [ ] **Bindings.** `EntityRef`, `EntityWatch`, `QueryRef` and `DerivedRef` move to `./lit`, keep extending `@a11d/lit`'s `Controller`, and take their store or repository explicitly instead of reaching for the installed client. Because `@a11d/lit` is enforced, `binder()` moves with `EntityRef`, and `ReactiveBinder` — which depends on nothing but `@a11d/lit` — moves with it; SIPA imports it from the package. A rejected write calls a hook instead of raising a toast.
- [ ] **SIPA wrappers.** SIPA keeps thin subclasses that default to the installed client and supply the toast, so its ~30 construction sites do not change.
- [ ] **SDK re-export.** `@pleiades/sdk` depends on the package and re-exports it, so existing `@pleiades/sdk` imports keep resolving.
- [ ] **Build.** In-repo consumers keep bundling from source, as they do for the SDK and SIPA. A `tsc` build to JavaScript plus declarations exists for outside consumers; the acceptance check installs the `npm pack` tarball into a scratch project with default compiler settings.
- [ ] **Documents.** The README moves with the package and is rewritten in domain-neutral terms around the fixture. A `.GENESIS.md` for the package; `sdk.ts/.GENESIS.md`, [[Clarification - Local Repo System]] and the Placement section of [[PEP106 - Frontend Repository System]] updated; the [[Phase 3A]] box ticked.

# Reserved for the error system
The error system (server-side validation, an error registry) is deferred. This plan only decides where a failure *passes through*, and keeps each of those places opaque, so that system can arrive later without reshaping the library.

**What this plan fixes:**
- A read can say "no answer" (the `unresolved` sentinel). That is a statement about reachability, not an error model.
- Whether a write succeeded is a predicate the consumer declares.
- A rejected write reaches the host through one hook rather than a hard-coded message.
- Data states (not loaded, loaded, removed) are facts about the store. They sit beside `error`, and say nothing about why a read or write failed.

**What it deliberately leaves open:**
- **The failure payload.** Wherever an outcome crosses the library — the sentinel, the write result, the rejection hook, `ref.error` — there is room for one opaque `failure` value the library carries and never inspects. Codes, a registry, validation detail and user-facing wording all live in that value and above the library.
- **Return shapes.** `commit` and `EntityDraft.commit` still return a boolean. If field-level validation needs them to return an outcome object, that change is made once, with the error system, rather than guessed at now — which is what the `0.x` version is for.
- **Field-level errors on binders and drafts**, **retry policy**, and **thrown versus returned failures** in the PLAINTORCH client — untouched.
- **The interim reachability adapter** of stage 3 is expected to be deleted, not evolved.

# Sequencing
Stages 1 → 2 → 3 are strictly ordered. Stage 4's remaining suites can run alongside stage 3. Stage 5's move can begin once stage 2 is in, but the package is not offered to another project before stage 3 and the fixture are done.
