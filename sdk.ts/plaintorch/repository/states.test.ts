import { describe, it, expect, vi, afterEach } from "vitest"
import { EntityStore } from "./entityStore"
import { EntityRepository } from "./entityRepository"
import { DerivedRepository } from "./derivedRepository"
import { RepositorySet } from "./repositorySet"
import type { InvalidationScheduler } from "./invalidation"
import { unresolved, type Unresolved } from "./outcome"
import { makeEntity, flushMicrotasks, testPolicy } from "./test-utils"

// What the store knows of an identity's data — not loaded, loaded, removed — and the three things a fetcher can
// answer with: a value, an authoritative nothing, or no answer at all. A removal and an outage both used to
// look like "nothing came back"; these suites pin that they no longer do.

type Thing = { "@type": string, id: string, title: string, status?: number }
type Answer = Record<string, unknown> | undefined | Unresolved

afterEach(() => {
	vi.useRealTimers()
})

describe("EntityStore — data states", () => {
	it("an identity is not loaded until absorbed, whether unheard of or merely observed", () => {
		const store = new EntityStore(testPolicy)
		expect(store.stateOf("Thing:1")).toBe("not-loaded")

		store.subscribe("Thing:1", () => { })
		expect(store.stateOf("Thing:1")).toBe("not-loaded")

		store.absorb(makeEntity("Thing", "1"))
		expect(store.stateOf("Thing:1")).toBe("loaded")
	})

	it("remove drops the instance, marks an observed identity removed and tells its subscribers", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		let notified = 0
		store.subscribe("Thing:1", () => notified++)
		const versionBefore = store.version("Thing:1")

		store.remove("Thing:1")

		expect(store.stateOf("Thing:1")).toBe("removed")
		expect(store.peek("Thing:1")).toBeUndefined()
		expect(store.has("Thing:1")).toBe(false)
		expect(store.entitiesOfType("Thing")).toEqual([])
		expect(store.version("Thing:1")).toBe(versionBefore + 1)
		expect(notified).toBe(1)
	})

	it("an observed identity that was never loaded can be removed too — the backend says it does not exist", () => {
		const store = new EntityStore(testPolicy)
		let notified = 0
		store.subscribe("Thing:1", () => notified++)

		store.remove("Thing:1")

		expect(store.stateOf("Thing:1")).toBe("removed")
		expect(notified).toBe(1)
	})

	it("an identity nothing observes leaves no record behind", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))

		store.remove("Thing:1")

		expect(store.recordCount).toBe(0)
		expect(store.keysOfType("Thing")).toEqual([])
		expect(store.stateOf("Thing:1")).toBe("not-loaded")
	})

	it("removing twice, or removing what the store has never heard of, does nothing", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		let notified = 0
		store.subscribe("Thing:1", () => notified++)

		store.remove("Thing:1")
		store.remove("Thing:1")
		store.remove("Thing:2")

		expect(notified).toBe(1)
		expect(store.stateOf("Thing:2")).toBe("not-loaded")
	})

	it("tells the type's observers, so a live collection drops the member", async () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		await flushMicrotasks()
		let notified = 0
		store.subscribeType("Thing", () => notified++)

		store.remove("Thing:1")
		await flushMicrotasks()

		expect(notified).toBe(1)
	})

	it("a removed identity absorbed again is loaded, as a new instance", () => {
		const store = new EntityStore(testPolicy)
		const first = store.absorb(makeEntity("Thing", "1"))
		let notified = 0
		store.subscribe("Thing:1", () => notified++)
		store.remove("Thing:1")

		const second = store.absorb(makeEntity("Thing", "1"))

		expect(store.stateOf("Thing:1")).toBe("loaded")
		expect(store.peek("Thing:1")).toBe(second)
		expect(second).not.toBe(first)
		expect(notified).toBe(2)
	})

	it("a response issued before the removal does not bring the entity back; one issued after does", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		store.subscribe("Thing:1", () => { })
		const before = store.beginRead()

		store.remove("Thing:1")
		const stale = makeEntity("Thing", "1")
		expect(store.absorb(stale, undefined, { issuedAt: before })).toBe(stale)
		expect(store.stateOf("Thing:1")).toBe("removed")
		store.endRead(before)

		const after = store.beginRead()
		store.absorb(makeEntity("Thing", "1"), undefined, { issuedAt: after })
		store.endRead(after)
		expect(store.stateOf("Thing:1")).toBe("loaded")
	})

	it("snapshot, patch, touch and restore are all inert on a removed identity", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1", { status: 0 }))
		store.subscribe("Thing:1", () => { })
		const snapshot = store.snapshot("Thing:1")!
		store.remove("Thing:1")

		expect(store.snapshot("Thing:1")).toBeUndefined()
		expect(() => {
			store.patch("Thing:1", { status: 3 })
			store.touch("Thing:1")
			store.restore("Thing:1", snapshot)
		}).not.toThrow()
		expect(store.stateOf("Thing:1")).toBe("removed")
	})

	it("sweeps a removed identity once nothing observes it", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		const release = store.subscribe("Thing:1", () => { })
		store.remove("Thing:1")
		expect(store.sweep()).toBe(0)

		release()
		expect(store.sweep()).toBe(1)
		expect(store.recordCount).toBe(0)
	})
})

describe("EntityStore — freshness stamps", () => {
	it("stamps a held identity, and nothing else", () => {
		const store = new EntityStore(testPolicy)
		store.subscribe("Thing:2", () => { })
		store.absorb(makeEntity("Thing", "1"))

		store.markResolved("Thing:1", 1234)
		store.markResolved("Thing:2", 1234)
		store.markResolved("Thing:3", 1234)

		expect(store.resolvedAt("Thing:1")).toBe(1234)
		expect(store.resolvedAt("Thing:2")).toBeUndefined()
		expect(store.resolvedAt("Thing:3")).toBeUndefined()
	})

	it("the stamp is evicted with its entity, and cleared by a removal", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		store.absorb(makeEntity("Thing", "2"))
		store.subscribe("Thing:2", () => { })
		store.markResolved("Thing:1")
		store.markResolved("Thing:2")

		store.sweep()
		store.remove("Thing:2")

		expect(store.resolvedAt("Thing:1")).toBeUndefined()
		expect(store.resolvedAt("Thing:2")).toBeUndefined()
	})
})

function entityHarness(...first: [Answer?]) {
	const store = new EntityStore(testPolicy)
	// A rest parameter, so an explicit `undefined` — the backend has nothing — is not mistaken for "use the default".
	let answer: Answer = first.length > 0 ? first[0] : makeEntity("Thing", "1", { status: 0 })
	let calls = 0
	const invalidated: string[] = []
	const invalidation = { invalidate: (typeName: string, id: string) => { invalidated.push(`${typeName}:${id}`) } } as unknown as InvalidationScheduler
	const fetcher = async () => {
		calls++
		return answer === undefined || answer === unresolved ? answer : store.absorb({ ...answer }, Object.keys(answer)) as Thing
	}

	return {
		store,
		fetcher,
		invalidation,
		invalidated,
		repository: new EntityRepository<Thing>(store, "Thing", fetcher, { invalidation }),
		answerWith: (next: Answer) => { answer = next },
		get calls() { return calls }
	}
}

describe("EntityRepository — read outcomes", () => {
	it("no answer keeps the entity that is held, and does not count as a fresh resolution", async () => {
		vi.useFakeTimers()
		const h = entityHarness()
		const thing = await h.repository.get("1")
		vi.advanceTimersByTime(60_000)

		h.answerWith(unresolved)
		expect(await h.repository.get("1")).toBe(thing)
		expect(h.repository.stateOf("1")).toBe("loaded")

		// Still past its window, so the next read asks again rather than trusting the failed one.
		await h.repository.get("1")
		expect(h.calls).toBe(3)
	})

	it("no answer for an identity never loaded leaves it not loaded", async () => {
		const h = entityHarness(unresolved)
		h.repository.subscribe("1", () => { })

		expect(await h.repository.get("1")).toBeUndefined()
		expect(h.repository.stateOf("1")).toBe("not-loaded")
	})

	it("an answer of nothing removes a held entity and tells its observers", async () => {
		const h = entityHarness()
		await h.repository.get("1")
		let notified = 0
		h.repository.subscribe("1", () => notified++)

		h.answerWith(undefined)
		expect(await h.repository.refresh("1")).toBeUndefined()

		expect(h.repository.stateOf("1")).toBe("removed")
		expect(h.repository.peek("1")).toBeUndefined()
		expect(notified).toBe(1)
	})

	it("an answer of nothing for an observed identity that never loaded marks it removed", async () => {
		const h = entityHarness(undefined)
		h.repository.subscribe("1", () => { })

		await h.repository.get("1")

		expect(h.repository.stateOf("1")).toBe("removed")
	})

	it("a removed identity is asked for again, and comes back when the backend has it", async () => {
		const h = entityHarness()
		await h.repository.get("1")
		h.repository.subscribe("1", () => { })
		h.answerWith(undefined)
		await h.repository.refresh("1")

		h.answerWith(makeEntity("Thing", "1", { status: 2 }))
		const back = await h.repository.get("1")

		expect(h.repository.stateOf("1")).toBe("loaded")
		expect(back?.status).toBe(2)
	})

	it("an answer of nothing does not remove an entity absorbed while the request was in flight", async () => {
		const store = new EntityStore(testPolicy)
		let release!: () => void
		const repository = new EntityRepository<Thing>(store, "Thing", async () => {
			await new Promise<void>((resolve) => { release = resolve })
			return undefined
		})
		store.subscribe("Thing:1", () => { })

		const request = repository.get("1")
		await flushMicrotasks()
		// A listing lands and names the entity while the older "not found" is still on its way.
		store.absorb(makeEntity("Thing", "1"))
		release()
		await request

		expect(repository.stateOf("1")).toBe("loaded")
	})

	it("an answer of nothing does not remove an entity changed while the request was in flight", async () => {
		const store = new EntityStore(testPolicy)
		const held = store.absorb(makeEntity("Thing", "1", { status: 0 })) as unknown as Thing
		let release!: () => void
		const repository = new EntityRepository<Thing>(store, "Thing", async () => {
			await new Promise<void>((resolve) => { release = resolve })
			return undefined
		})

		const request = repository.refresh("1")
		await flushMicrotasks()
		store.absorb(makeEntity("Thing", "1", { status: 5 }))
		release()
		await request

		expect(repository.peek("1")).toBe(held)
	})

	it("revalidateObserved refetches what this repository resolved, not what only arrived in a listing", async () => {
		const h = entityHarness()
		await h.repository.get("1")
		h.repository.subscribe("1", () => { })
		h.store.absorb(makeEntity("Thing", "listed"))
		h.repository.subscribe("listed", () => { })

		await h.repository.revalidateObserved()

		expect(h.calls).toBe(2)
	})

	it("an invalidated entity stays eligible for revalidateObserved", async () => {
		const h = entityHarness()
		await h.repository.get("1")
		h.repository.subscribe("1", () => { })
		h.repository.invalidate("1")

		await h.repository.revalidateObserved()

		expect(h.calls).toBe(2)
	})
})

describe("EntityRepository — write success is the repository's to declare", () => {
	function harness(succeeded?: (result: unknown) => boolean) {
		const h = entityHarness()
		return { ...h, repository: new EntityRepository<Thing>(h.store, "Thing", h.fetcher, { invalidation: h.invalidation, succeeded }) }
	}

	it("by default a write that answers nothing is a rejection, on every write path", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const snapshot = h.repository.snapshot("1")

		thing.status = 9
		expect(await h.repository.commit("1", async () => undefined, snapshot)).toBe(false)
		expect(thing.status).toBe(0)

		const draft = h.repository.fork("1")!
		draft.value.status = 7
		expect(await draft.commit(async () => undefined)).toBe(false)
		expect(thing.status).toBe(0)
		expect(h.invalidated).toEqual([])
	})

	it("a repository whose backend answers writes with nothing accepts them on every write path", async () => {
		const h = harness(() => true)
		const thing = (await h.repository.get("1"))!

		await h.repository.mutate("1", async () => undefined)
		expect(h.invalidated).toEqual(["Thing:1"])

		const snapshot = h.repository.snapshot("1")
		thing.status = 9
		expect(await h.repository.commit("1", async () => undefined, snapshot)).toBe(true)
		expect(thing.status).toBe(9)

		const draft = h.repository.fork("1")!
		draft.value.status = 7
		expect(await draft.commit(async () => undefined)).toBe(true)
		expect(thing.status).toBe(7)
	})

	it("a single mutate can still override the repository's test", async () => {
		const h = harness(() => true)
		await h.repository.get("1")

		await h.repository.mutate("1", async () => "refused", { succeeded: (result) => result !== "refused" })

		expect(h.invalidated).toEqual([])
	})

	it("a set hands its test to every entity repository that does not declare its own", async () => {
		const store = new EntityStore(testPolicy)
		const set = new RepositorySet(store, { succeeded: () => true })
		const lenient = set.entity<Thing>("Thing", async (id) => store.absorb(makeEntity("Thing", id)) as Thing)
		const strict = set.entity<Thing>("Other", async (id) => store.absorb(makeEntity("Other", id)) as Thing, { succeeded: () => false })
		await lenient.get("1")
		await strict.get("1")

		expect(await lenient.commit("1", async () => undefined)).toBe(true)
		expect(await strict.commit("1", async () => "anything")).toBe(false)
	})
})

describe("DerivedRepository — read outcomes", () => {
	function harness<T>(first: T | undefined | Unresolved, options?: { freshnessMs?: number }) {
		let answer: T | undefined | Unresolved = first
		let calls = 0
		const repository = new DerivedRepository<T>(new EntityStore(testPolicy), async () => {
			calls++
			return answer
		}, options)
		return { repository, answerWith: (next: T | undefined | Unresolved) => { answer = next }, get calls() { return calls } }
	}

	it("no answer keeps the last resolved value, notifies nobody and leaves the record stale", async () => {
		const h = harness<string[]>(["a", "b"])
		let notified = 0
		h.repository.subscribe("", () => notified++)
		await h.repository.get()

		h.answerWith(unresolved)
		expect(await h.repository.refresh()).toEqual(["a", "b"])

		expect(h.repository.peek()).toEqual(["a", "b"])
		expect(notified).toBe(1)
		// Stale, so the next read asks again instead of serving the kept value as fresh.
		await h.repository.get()
		expect(h.calls).toBe(3)
	})

	it("an answer of nothing empties the record and notifies", async () => {
		const h = harness<string[]>(["a", "b"])
		let notified = 0
		h.repository.subscribe("", () => notified++)
		await h.repository.get()

		h.answerWith(undefined)
		await h.repository.refresh()

		expect(h.repository.peek()).toBeUndefined()
		expect(notified).toBe(2)
	})

	it("tells never resolved from resolved to nothing", async () => {
		const empty = harness<string>(undefined)
		const silent = harness<string>(unresolved)
		expect(empty.repository.isResolved()).toBe(false)

		await empty.repository.get()
		await silent.repository.get()

		expect(empty.repository.isResolved()).toBe(true)
		expect(silent.repository.isResolved()).toBe(false)
	})

	it("a record that got no answer recovers on the next read", async () => {
		const h = harness<string>(unresolved)
		await h.repository.get()

		h.answerWith("there")
		expect(await h.repository.get()).toBe("there")
		expect(h.repository.isResolved()).toBe(true)
	})
})

describe("DerivedRepository — pruning", () => {
	it("drops unobserved records that are expired, stale or never resolved, and keeps the rest", async () => {
		vi.useFakeTimers()
		const store = new EntityStore(testPolicy)
		const repository = new DerivedRepository<string>(store, async (key) => key, { freshnessMs: 1_000 })
		await repository.get("expired")
		vi.advanceTimersByTime(600)
		await repository.get("fresh")
		await repository.get("stale")
		await repository.get("observed")
		repository.subscribe("observed", () => { })
		repository.subscribe("never-resolved", () => { })()
		repository.invalidate("stale")
		vi.advanceTimersByTime(600)

		expect(repository.prune()).toBe(3)

		expect(repository.peek("expired")).toBeUndefined()
		expect(repository.peek("stale")).toBeUndefined()
		expect(repository.isResolved("never-resolved")).toBe(false)
		expect(repository.peek("fresh")).toBe("fresh")
		expect(repository.peek("observed")).toBe("observed")
	})

	it("leaves a record alone while it is being fetched", async () => {
		const store = new EntityStore(testPolicy)
		let release!: (value: string) => void
		const repository = new DerivedRepository<string>(store, async () => await new Promise<string>((resolve) => { release = resolve }))
		repository.subscribe("a", () => { })()

		const request = repository.get("a")
		expect(repository.prune()).toBe(0)

		release("done")
		expect(await request).toBe("done")
		expect(repository.peek("a")).toBe("done")
	})

	it("a set's sweep prunes first, so an expired unobserved view no longer pins its entities", async () => {
		vi.useFakeTimers()
		const store = new EntityStore(testPolicy)
		const set = new RepositorySet(store)
		const task = store.absorb(makeEntity("Task", "T1"))
		const listing = set.derived(async () => [task], { freshnessMs: 1_000 })
		await listing.get()
		expect(set.sweep()).toBe(0)

		vi.advanceTimersByTime(1_001)
		expect(set.sweep()).toBe(1)
		expect(store.has("Task:T1")).toBe(false)
	})

	it("an observed view keeps pinning its entities however old it is", async () => {
		vi.useFakeTimers()
		const store = new EntityStore(testPolicy)
		const set = new RepositorySet(store)
		const task = store.absorb(makeEntity("Task", "T1"))
		const listing = set.derived(async () => [task], { freshnessMs: 1_000 })
		listing.subscribe("", () => { })
		await listing.get()

		vi.advanceTimersByTime(60_000)
		expect(set.sweep()).toBe(0)
		expect(store.peek("Task:T1")).toBe(task)
	})
})

describe("RepositorySet — removal", () => {
	it("remove marks the identity removed; a change feed's deletion reaches it through the sink", () => {
		const store = new EntityStore(testPolicy)
		const set = new RepositorySet(store)
		const things = set.entity<Thing>("Thing", async () => undefined)
		store.absorb(makeEntity("Thing", "1"))
		things.subscribe("1", () => { })

		set.remove("Thing", "1")

		expect(things.stateOf("1")).toBe("removed")
	})
})

describe("EntityStore.wasRemoved — asked of an instance someone holds", () => {
	it("is true for the instance that was canonical when its entity was removed, observed or not", () => {
		const store = new EntityStore(testPolicy)
		const observed = store.absorb(makeEntity("Thing", "1"))
		const unobserved = store.absorb(makeEntity("Thing", "2"))
		const untouched = store.absorb(makeEntity("Thing", "3"))
		store.subscribe("Thing:1", () => { })

		store.remove("Thing:1")
		store.remove("Thing:2")

		expect(store.wasRemoved(observed)).toBe(true)
		// Nothing observed it, so the store kept no record of the identity — the instance still knows.
		expect(store.stateOf("Thing:2")).toBe("not-loaded")
		expect(store.wasRemoved(unobserved)).toBe(true)
		expect(store.wasRemoved(untouched)).toBe(false)
	})

	it("stays true for the old instance when the identity is absorbed again; the new instance is not removed", () => {
		const store = new EntityStore(testPolicy)
		const old = store.absorb(makeEntity("Thing", "1"))
		store.remove("Thing:1")

		const fresh = store.absorb(makeEntity("Thing", "1"))

		expect(store.wasRemoved(old)).toBe(true)
		expect(store.wasRemoved(fresh)).toBe(false)
	})

	it("answers for a single reference another entity still holds, which detaching leaves in place", () => {
		const store = new EntityStore(testPolicy)
		const user = store.absorb(makeEntity("User", "U1"))
		const task = store.absorb(makeEntity("Task", "T1", { assignee: user, watchers: [user] })) as Record<string, unknown>

		store.remove("User:U1")

		expect(task.watchers).toEqual([])
		expect(task.assignee).toBe(user)
		expect(store.wasRemoved(task.assignee)).toBe(true)
	})

	it("reads a never-canonical copy of a removed identity as removed while the identity is", () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1"))
		store.subscribe("Thing:1", () => { })
		store.remove("Thing:1")

		expect(store.wasRemoved(makeEntity("Thing", "1"))).toBe(true)
	})

	it("is false for anything that is not a removed entity", () => {
		const store = new EntityStore(testPolicy)
		expect(store.wasRemoved(undefined)).toBe(false)
		expect(store.wasRemoved(null)).toBe(false)
		expect(store.wasRemoved("Thing:1")).toBe(false)
		expect(store.wasRemoved({ plain: true })).toBe(false)
		expect(store.wasRemoved(makeEntity("Thing", "never-seen"))).toBe(false)
	})
})