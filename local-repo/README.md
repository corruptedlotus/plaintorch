# @corruptedlotus/local-repo

A single identity-keyed store of canonical entity instances, typed repositories over whatever backend you
have, and a reactivity model that keeps every surface showing the same entity in step — the frontend
counterpart of a backend that is the one authority over its data and has many observers.

It knows nothing about your domain. You tell it what an entity is; it keeps one instance per identity, merges
every response into that instance, tells whoever is observing, and takes care of the parts that go wrong when
this is done by hand: a slow read reverting a fresh edit, a rejected write that looked accepted, a deleted entity
lingering in a list, a store that only ever grows.

```
npm install @corruptedlotus/local-repo
npm install @a11d/lit          # only for the `/lit` bindings
```

| Entry | What it is | Depends on |
|---|---|---|
| `@corruptedlotus/local-repo` | the store, repositories, drafts, invalidation, change feed | nothing |
| `@corruptedlotus/local-repo/lit` | `EntityRef`, `EntityWatch`, `QueryRef`, `DerivedRef`, `ReactiveBinder` — controllers for `@a11d/lit` components | `@a11d/lit` (peer) |
| `@corruptedlotus/local-repo/testing` | the fakes the package's own suites use | nothing |

---

## Mental model

It behaves like an ORM's identity map (EF Core's `DbContext`, MikroORM's unit of work), on the client:

| ORM concept | Here |
|---|---|
| Identity map / first-level cache | `EntityStore` — one canonical instance per `{type}:{id}` |
| `DbSet<T>` / typed access | `EntityRepository<T>` — read and write one type against the store |
| Tracked reference | `EntityRef<T>` — a component's live handle to one entity |
| Query | `QueryRef<T>` — a live filtered view over one type already in the store |
| Detached entity / working copy | `EntityDraft<T>` — a forked, isolated copy committed later |
| Change tracker flush | the write cycle: optimistic apply, send, invalidate or roll back |

The load-bearing idea: **responses are absorbed, not returned.** Every response is walked as it is parsed, and
each entity in it is merged into the instance already in the store. So every surface holding a reference is
looking at current state by construction; keeping them in agreement is just telling them to re-render.

---

## The pieces

- `EntityStore` — the identity map. Built with a policy. Absorb, peek, subscribe (per identity, per type, all),
  `touch` to announce an in-place edit, `remove`, the data states, the ordering ledger, the eviction sweep.
- `EntityPolicy` — what the store is told about your domain: `identify` (which values are entities, under which
  key), `models` (how a payload becomes an instance of your class, if you have classes), `mergeableKeys` (which
  fields of a payload are trusted), `removal` (what happens to the collections that held a removed entity).
- `EntityRepository<T>` — one per entity type: `get` / `refresh` with freshness and in-flight de-duplication,
  `mutate`, `commit`, `fork`, `snapshot`, `subscribe`, `stateOf`.
- `DerivedRepository<T>` — cached values that are not entities themselves (a listing, a dashboard) but contain
  them; absorbed for the entities inside, so a listing and a detail view of one of its rows agree.
- `EntityDraft<T>` — the fork-and-commit working copy.
- `InvalidationScheduler` — what a change makes stale, coalesced onto a microtask; revalidates what is observed.
- `ChangeFeed` — turns a stream of change events from the backend into invalidations and removals, carrying
  writes this client did not make back into the store. Knows only a *source* (open a line stream) and a *sink*.
- `RepositorySet` — creates the repositories and owns what spans them: routing by type name, invalidation, the
  feed's sink, the eviction sweep. `set.entity(...)` and `set.derived(...)` are how a repository comes to exist.
- `store.read(...)` — the one call you wrap around a request whose response is absorbed.
- `unresolved` — what a fetcher answers when no trustworthy answer came back.

---

## How to use it

The example is the package's own acceptance fixture, `foreignDomain.test.ts`: a bookshop whose entities carry a
`kind` and are identified by an `isbn` or a `slug`, whose writes answer with nothing, and whose backend announces
changes on a server-sent event stream.

### 1. Say what an entity is

```ts
import { EntityStore, entityKey, type EntityPolicy } from "@corruptedlotus/local-repo"

const policy: EntityPolicy = {
	identify: (value) => {
		const record = value as { kind?: unknown, isbn?: unknown, slug?: unknown } | null
		if (record?.kind === "book" && typeof record.isbn === "string") return entityKey("Book", record.isbn)
		if (record?.kind === "author" && typeof record.slug === "string") return entityKey("Author", record.slug)
		return undefined
	}
}
const store = new EntityStore(policy)
```

`identify` is the one thing every consumer writes. It is called for every object in every response, so it must
be cheap and must say `undefined` for anything that is not an entity — value objects, child records, references
that carry another entity's id. For the common shape there is a preset: `identifyByShape({ typeKey: "kind",
idKey: "id", requires?: (value) => boolean })`.

The rest of the policy is optional:

- **`models`** — if your entities are class instances, how to construct one from a payload and whether a type
  name has a class. Without it, entities are the plain objects they were parsed as.
- **`mergeableKeys(payload, isRoot)`** — which fields of a payload the store may write over the instance. The
  default trusts every field. Declare otherwise when your backend serializes something it did not load as if it
  were empty — the bookshop's nested authors come with `books: []`, which must not wipe the books a direct read
  of the author filled in:

  ```ts
  mergeableKeys: (payload, isRoot) =>
  	Object.keys(payload).filter((field) => isRoot || !(Array.isArray(payload[field]) && payload[field].length === 0))
  ```

- **`removal`** — `"detach"` (default) or `"keep"`, or a function of the type name. See *Removal* below.

### 2. Read through the store

Every request whose response is absorbed goes through `store.read`. It hands you a `parse` that absorbs as it
parses, and brackets the request in the store's ordering ledger, so a response that lands after a local edit
cannot revert it.

```ts
import { unresolved } from "@corruptedlotus/local-repo"

const read = async <T>(path: string) => await store.read(async (parse) => {
	const response = await fetch(path)
	if (response.ok) return parse<T>(await response.text())
	return response.status === 404 ? undefined : unresolved
})
```

A fetcher has exactly three answers, and the repository does something different with each:

| It resolves to | It means | Entity repository | Derived repository |
|---|---|---|---|
| a value | here it is | absorbed, stamped fresh | cached, observers told if it changed |
| `undefined` | the backend says there is nothing | the entity is **removed** | the record is emptied |
| `unresolved` | no trustworthy answer came back | kept as it was, still stale | last value kept, nobody told |

**Never let a failure come back as `undefined`.** It is taken at its word and removes the entity; an outage would
read as a mass deletion. Translate at the client boundary, as above.

### 3. Declare the repositories

```ts
import { RepositorySet } from "@corruptedlotus/local-repo"

const repos = new RepositorySet(store, {
	// The shop answers writes with a 204, so "nothing came back" is success here. Only an explicit `false` —
	// the consumer's own way of reporting a refusal — is a rejection.
	succeeded: (result) => result !== false,
	// A book's author lists that book, so a change to a book makes its authors stale.
	dependents: { Book: (book) => book.authors.map((author) => entityKey("Author", author.slug)) }
})
const books = repos.entity<Book>("Book", (isbn) => read<Book>(`/books/${isbn}`))
const authors = repos.entity<Author>("Author", (slug) => read<Author>(`/authors/${slug}`))
const catalogue = repos.derived<Book[]>(() => read<Book[]>("/books"))
```

Always create repositories through the set. That is what routes a type name to its repository, makes a listing a
revalidation target, and makes it a root for the eviction sweep. A `DerivedRepository` constructed by hand is
invisible to the sweep, which would evict the entities it holds out from under it.

`succeeded` is the **write-success test**: given what your persist call returned, did the backend accept the
write? The default treats an absent result as a rejection, which suits a client that reports failure by
returning nothing. It can also be set per repository.

### 4. Read, write, fork

```ts
const book = await books.get("978-0")          // fetched once; served from the store while fresh
await books.refresh("978-0")                   // regardless of freshness
books.stateOf("978-0")                         // "not-loaded" | "loaded" | "removed"

// An imperative write whose optimistic edit you apply yourself:
await books.mutate("978-0", async () => shop.patch("978-0", { price: 15 }), (book) => { book.price = 15 })

// An edit already applied in place (a two-way binding did it): announce, send, roll back if rejected.
const snapshot = books.snapshot("978-0")
book.price = 15
const accepted = await books.commit("978-0", async () => shop.patch("978-0", { price: 15 }), snapshot)

// A multi-field or cancellable edit: an isolated copy, nothing reaches the store until commit.
const draft = books.fork("978-0")!
draft.value.price = 20
draft.value.name = "The Dispossessed (revised)"
if (draft.dirty) await draft.commit((patch, book) => shop.patch(book.isbn, patch))   // sends only the diff
```

A draft diffs against its fork-time baseline, so it sends only the fields it changed, and a concurrent edit to a
field the draft never touched survives the commit. Snapshots and drafts are **shallow**: a nested array is shared
with the canonical instance. Edit collections through explicit actions, never by mutating a draft's array.

### 5. Carry the backend's own changes in

```ts
import { ChangeFeed } from "@corruptedlotus/local-repo"

const feed = new ChangeFeed({ open: async (signal) => shop.stream(signal) }, repos, { initialRetryDelayMs: 500 })
feed.start()
```

The source opens an async iterable of lines; the feed parses `data:` lines as JSON change events
(`{ type, id, operation }`, with `Deleted` removing the entity at once) and drives the set: the changed entity is
refetched if anything observes it, what depends on it is revalidated, and a dropped stream is reopened with
backoff. `parseJsonChange` is exported for a source that frames its events differently.

### 6. Bind components (`/lit`)

Hosts are `@a11d/lit` components; the bindings are its `Controller`s. Each owns the whole lifecycle a component
used to hand-roll — resolve, observe, re-render on change, release on disconnect — and exposes the data states
beside `loading` and `error`.

```ts
import { EntityRef, EntityWatch, QueryRef, DerivedRef } from "@corruptedlotus/local-repo/lit"

@component("shop-book")
class BookView extends Component {
	@property() isbn = ""
	readonly ref = new EntityRef<Book>(this, () => books, () => this.isbn || undefined, {
		rejected: () => notify("The shop did not accept that change.")
	})
	readonly binder = this.ref.binder("entity", {
		price: (book) => shop.patch(book.isbn, { price: book.price }),
		"*": (book, field) => shop.patch(book.isbn, { [field]: book[field] })
	})
	get entity() { return this.ref.value }

	get template() {
		if (this.ref.removed) return html`<p>This book is no longer in the catalogue.</p>`
		if (this.ref.notLoaded && !this.ref.loading) return html`<p>The shop could not be reached. <button @click=${() => this.ref.refresh()}>Retry</button></p>`
		return html`<input ${this.binder.bind("name")}> <input type="number" ${this.binder.bind("price")}>`
	}
}
```

- **`EntityRef<T>(host, repository, id, options?)`** resolves an entity by identifier. Both the repository and
  the identifier are thunks, read on every host update, because they usually arrive as properties and can change
  while the host stays mounted. `value`, `loading`, `error`, `notLoaded`, `removed`, `refresh()`, `fork()`.
- **`ref.binder(sourceKey, persist, reaction?)`** returns a two-way binder whose every edit runs the full cycle:
  snapshot, write-through to the canonical instance, broadcast, send through the `persist` map (`'*'` is the
  fallback, told which field changed), roll back if rejected, and the `rejected` hook — the library raises no
  notification of its own. `reaction` runs after each commit with the field, the entity and whether it saved.
- **`EntityWatch(host, store, instance)`** observes an entity the host was *handed* — a list item — rather than one
  it resolves. `removed` says whether that very instance has since been removed, and is reflected on the host as
  the `entity-removed` attribute; `publish()` announces an in-place edit.
- **`QueryRef<T>(host, repository, predicate?, compare?)`** is a live, ordered, filtered slice of one type in the
  store. Not a fetch: load the members as usual, and this keeps the slice current as members change, enter or
  leave the predicate. The predicate and comparator are read on every access.
- **`DerivedRef<T>(host, repository, key?)`** binds a derived record: `value`, `loading`, `error`, `notLoaded`,
  `refresh()`.
- **`ReactiveBinder<T>`** is the binder the reference builds — an `@a11d/lit` `Binder` with `sourceUpdate` /
  `sourceUpdated` hooks — for a host that wants the cycle on a property of its own.

The bindings are plain classes without decorators, but their suites declare test hosts with `@a11d/lit`'s legacy
decorators, which need `experimentalDecorators` and `useDefineForClassFields: false` in whatever transforms them.

---

## Data states, removal, and what observers see

The store holds one of three **data states** per identity (`store.stateOf(key)`, `repository.stateOf(id)`):
`not-loaded` (nothing has answered for it), `loaded` (the canonical instance is held), `removed` (the backend
answered that it does not exist). A derived record has no identity; `repository.isResolved(key)` tells "never
answered" from "resolved to nothing".

**Removal.** `store.remove(key)` — reached by a fetcher's `undefined`, a feed's `Deleted`, or a direct call —
drops the instance, tells the identity's observers and its type's, and is noted in the ordering ledger so a
response issued *before* it (a listing still in flight) cannot bring it back. What held the entity is the
policy's `removal`:

- `"detach"` (default): the entity is taken out of every collection the store can reach — the collections of the
  other entities it holds, including those inside their value objects, and the values of the derived records over
  it. A listing loses the row and an owner loses the member at the moment of removal, not at the next refetch;
  the collection is replaced by a new array (so a host handed it as a property sees a new reference), the owner
  is left stale, and whatever observes them is told. Single references are never cleared: whether an owner can do
  without one is not the store's to know.
- `"keep"`: nothing else is touched; holders go on holding the instance until they refresh — for a domain that
  wants to *show* a removal rather than lose the row.

Whatever still holds the instance can ask `store.wasRemoved(instance)`. The mark is on the instance (held
weakly), not the identity, so it answers for a host that mounts afterwards and after the identity has been
absorbed again as a new instance. `EntityWatch.removed` is this.

**Propagation.** An edit a binding writes into the canonical instance is already applied everywhere; `touch`
tells the observers. A write's success refetches the entity if observed and revalidates what depends on it; the
feed reaches the owners a local dependency table cannot see. There is nothing to fan out by hand.

**Ordering.** Every read captures the store's revision when issued; a local change bumps it. A response is
discarded for an identity that changed *after* the response was issued, so a slow read never reverts an edit
made while it was in flight. The ledger prunes itself: a marker is dropped once no in-flight read predates it.

**Bounding the map.** The store must actively forget. `repos.sweep()` (periodically with
`startEvictionSweep`) drops what nothing needs: it first prunes derived records nothing observes whose value
would not be served again (stale, expired, never resolved), then keeps everything a surviving record or a
subscribed entity reaches, and evicts the rest. An entity an *observed* listing holds is never evicted. Holding a
view without observing it — the array a one-shot `get()` returned, kept in a variable — is outside the
canonical-instance guarantee.

---

## Gotchas

- **`undefined` from a fetcher is a deletion.** Say `unresolved` when you do not know.
- **Snapshots and drafts are shallow.** Edit collections through explicit actions.
- **Create repositories through the set,** or the sweep cannot see what they hold.
- **A response already in flight can show a ghost:** a listing issued before a removal, landing after it, is not
  absorbed for that identity, but the raw row it carried stays in the listing until it is next refreshed.
- **Freshness is per repository** (entities 30 s, derived 15 s by default); `refresh` bypasses it.
- **A drifted type name renders blank, loudly:** an `EntityRef` asked for a type no repository serves warns once
  to the console.
- **Two copies of Lit** in one bundle break the bindings in confusing ways. When this package is linked from
  source beside another project that also installs `@a11d/lit`, alias or dedupe it onto one copy.

---

## Development

- `npm test` — Vitest. The core suites run under Node; the `lit/` suites mount real hosts under happy-dom.
- `npm run typecheck`; `npm run build` emits `dist/` (JavaScript plus declarations) for consumers that install
  the package. In-repository consumers resolve the `source` export condition and bundle the TypeScript directly.
- `foreignDomain.test.ts` is the acceptance test: the bookshop above runs the whole cycle — read, write, feed,
  removal, eviction — through the generic pieces alone. `policy.test.ts` guards that the core imports nothing
  from outside the package and the bindings nothing but `@a11d/lit`.
- `.GENESIS.md` records how the package came to be and how it works inside.
