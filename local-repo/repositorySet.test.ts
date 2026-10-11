import { describe, it, expect, vi, afterEach } from "vitest"
import { EntityStore } from "./entityStore.js"
import { RepositorySet } from "./repositorySet.js"
import { ChangeFeed } from "./changeFeed.js"
import { entityKey, identifyByShape } from "./identity.js"
import { plainModels } from "./policy.js"
import { makeEntity, testPolicy } from "./testing.js"

// The set is where a repository becomes known to everything that spans repositories: routing by type name,
// invalidation, the change feed and the eviction sweep. These suites build one over a small domain of tasks
// and projects, with fetchers that answer from a table the test edits.

type Row = Record<string, unknown>

function harness() {
	const store = new EntityStore(testPolicy)
	const rows = new Map<string, Row>()
	const fetched: string[] = []
	const put = (typeName: string, id: string, extra: Row = {}) => rows.set(`${typeName}:${id}`, makeEntity(typeName, id, extra))
	const fetcher = (typeName: string) => async (id: string) => {
		fetched.push(`${typeName}:${id}`)
		const row = rows.get(`${typeName}:${id}`)
		return row ? store.absorb({ ...row }, Object.keys(row)) as Row : undefined
	}

	const set = new RepositorySet(store, {
		dependents: { Task: (task) => [typeof task.projectId === "string" ? entityKey("Project", task.projectId) : undefined] }
	})
	const tasks = set.entity<Row>("Task", fetcher("Task"))
	const projects = set.entity<Row>("Project", fetcher("Project"))

	return { store, set, tasks, projects, rows, put, fetched }
}

afterEach(() => {
	vi.useRealTimers()
})

describe("RepositorySet — entity repositories", () => {
	it("routes a type name to the repository created for it", () => {
		const { set, tasks, projects } = harness()
		expect(set.forTypeName("Task")).toBe(tasks)
		expect(set.forTypeName("Project")).toBe(projects)
		expect(set.forTypeName("Nothing")).toBeUndefined()
		expect(set.forTypeName(undefined)).toBeUndefined()
	})

	it("refuses to route a type the store's policy has no model for", () => {
		const store = new EntityStore({
			identify: identifyByShape({ typeKey: "@type" }),
			models: { ...plainModels, isRegistered: (typeName) => typeName === "Task" }
		})
		const set = new RepositorySet(store)

		expect(() => set.entity("Task", async () => undefined)).not.toThrow()
		expect(() => set.entity("Project", async () => undefined)).toThrow(/"Project"/)
		expect(set.forTypeName("Project")).toBeUndefined()
	})

	it("hands each repository the set's invalidation, so a write revalidates what depends on it", async () => {
		const { set, tasks, projects, put, fetched } = harness()
		put("Task", "T1", { projectId: "P1" })
		put("Project", "P1")
		await tasks.get("T1")
		await projects.get("P1")
		projects.subscribe("P1", () => { })
		fetched.length = 0

		await tasks.mutate("T1", async () => true)
		await set.invalidation.settled()

		// The task itself is unobserved, so only its observed dependent is refetched.
		expect(fetched).toEqual(["Project:P1"])
	})

	it("passes a repository's own options through", async () => {
		vi.useFakeTimers()
		const { set, put, fetched } = harness()
		put("Note", "N1")
		const store = set.store
		const notes = set.entity<Row>("Note", async (id) => {
			fetched.push(`Note:${id}`)
			return store.absorb(makeEntity("Note", id)) as Row
		}, { freshnessMs: 10 })

		await notes.get("N1")
		vi.advanceTimersByTime(11)
		await notes.get("N1")
		expect(fetched).toEqual(["Note:N1", "Note:N1"])
	})

	it("revalidates an observed entity by key, splitting on the first colon only", async () => {
		const { set, tasks, put, fetched } = harness()
		put("Task", "a:b")
		await tasks.get("a:b")
		tasks.subscribe("a:b", () => { })
		fetched.length = 0

		await set.revalidateEntityIfObserved("Task:a:b")
		await set.revalidateEntityIfObserved("Nothing:x")

		expect(fetched).toEqual(["Task:a:b"])
	})
})

describe("RepositorySet — derived records", () => {
	it("revalidates the observed records after a write, and leaves the rest stale", async () => {
		const { set, tasks, put } = harness()
		put("Task", "T1")
		await tasks.get("T1")
		let observedCalls = 0
		let unobservedCalls = 0
		const observed = set.derived(async () => ++observedCalls)
		const unobserved = set.derived(async () => ++unobservedCalls)
		observed.subscribe("", () => { })
		await observed.get()
		await unobserved.get()

		await tasks.mutate("T1", async () => true)
		await set.invalidation.settled()
		expect(observedCalls).toBe(2)
		expect(unobservedCalls).toBe(1)

		// Left stale rather than fresh: its next reader pays for the fetch.
		await unobserved.get()
		expect(unobservedCalls).toBe(2)
	})

	it("invalidateAll marks every entity and every record stale without fetching", async () => {
		const { set, tasks, put, fetched } = harness()
		put("Task", "T1")
		await tasks.get("T1")
		let calls = 0
		const listing = set.derived(async () => ++calls)
		await listing.get()
		fetched.length = 0

		set.invalidateAll()
		expect(fetched).toEqual([])
		expect(calls).toBe(1)
		expect(set.store.isStale("Task:T1")).toBe(true)

		await listing.get()
		await tasks.get("T1")
		expect(calls).toBe(2)
		expect(fetched).toEqual(["Task:T1"])
	})

	it("revalidateObserved refetches observed entities and observed records together", async () => {
		const { set, tasks, put, fetched } = harness()
		put("Task", "T1")
		put("Task", "T2")
		await tasks.get("T1")
		await tasks.get("T2")
		tasks.subscribe("T2", () => { })
		let calls = 0
		const listing = set.derived(async () => ++calls)
		listing.subscribe("", () => { })
		await listing.get()
		fetched.length = 0

		await set.revalidateObserved()

		expect(fetched).toEqual(["Task:T2"])
		expect(calls).toBe(2)
	})
})

describe("RepositorySet — ordering", () => {
	it("acceptAuthority gives up the local claim, so a response issued before the edit applies", () => {
		const { set } = harness()
		const store = set.store
		const task = store.absorb(makeEntity("Task", "T1", { status: 0 })) as Row

		const issuedAt = store.currentRevision
		task.status = 9
		store.touch("Task:T1")
		store.absorb(makeEntity("Task", "T1", { status: 5 }), undefined, { issuedAt })
		expect(task.status).toBe(9)

		set.acceptAuthority("Task", "T1")
		store.absorb(makeEntity("Task", "T1", { status: 5 }), undefined, { issuedAt })
		expect(task.status).toBe(5)
	})

	it("drives a change feed: an event for an observed entity refetches it, a critical one over a local edit", async () => {
		vi.useFakeTimers()
		const { set, tasks, put, rows, fetched } = harness()
		put("Task", "T1", { status: 0 })
		const task = (await tasks.get("T1"))!
		tasks.subscribe("T1", () => { })
		fetched.length = 0

		async function* events(): AsyncIterable<string> {
			yield `data: ${JSON.stringify({ type: "Task", id: "T1", operation: "Modified", critical: true })}`
			yield ""
		}
		rows.set("Task:T1", makeEntity("Task", "T1", { status: 5 }))
		const feed = new ChangeFeed({ open: async () => events() }, set)

		feed.start()
		await vi.advanceTimersByTimeAsync(0)
		await set.invalidation.settled()
		feed.stop()

		// Once for the resynchronisation on connect, once for the event.
		expect(fetched).toEqual(["Task:T1", "Task:T1"])
		expect(task.status).toBe(5)
	})
})

describe("RepositorySet — eviction", () => {
	it("keeps what any resolved record holds and evicts the rest — every derived repository is a root", async () => {
		const { set } = harness()
		const store = set.store
		const first = store.absorb(makeEntity("Task", "T1"))
		const second = store.absorb(makeEntity("Task", "T2"))
		store.absorb(makeEntity("Task", "T3"))
		const listing = set.derived(async () => [first])
		const keyed = set.derived(async (key) => key === "b" ? { nested: { member: second } } : undefined)
		await listing.get()
		await keyed.get("a")
		await keyed.get("b")

		expect(set.sweep()).toBe(1)
		expect(store.peek("Task:T1")).toBe(first)
		expect(store.peek("Task:T2")).toBe(second)
		expect(store.has("Task:T3")).toBe(false)
	})

	it("an unobserved, unresolved or emptied record pins nothing", async () => {
		const { set } = harness()
		const store = set.store
		const task = store.absorb(makeEntity("Task", "T1"))
		let members: unknown[] = [task]
		const listing = set.derived(async () => members)
		set.derived(async () => [task]) // never resolved
		await listing.get()
		expect(set.sweep()).toBe(0)

		members = []
		await listing.refresh()
		expect(set.sweep()).toBe(1)
	})

	it("keeps a subscribed entity that appears in no record", async () => {
		const { set, tasks } = harness()
		set.store.absorb(makeEntity("Task", "T1"))
		tasks.subscribe("T1", () => { })
		await set.derived(async () => []).get()

		expect(set.sweep()).toBe(0)
		expect(set.store.has("Task:T1")).toBe(true)
	})

	it("sweeps on each interval once started, not before the first, and not after stop", () => {
		vi.useFakeTimers()
		const { set } = harness()
		const sweep = vi.spyOn(set, "sweep")
		expect(set.evictionSweepRunning).toBe(false)

		set.startEvictionSweep(1000)
		set.startEvictionSweep(1000) // ignored while one is running
		expect(set.evictionSweepRunning).toBe(true)
		vi.advanceTimersByTime(999)
		expect(sweep).not.toHaveBeenCalled()
		vi.advanceTimersByTime(2001)
		expect(sweep).toHaveBeenCalledTimes(3)

		set.stopEvictionSweep()
		set.stopEvictionSweep() // safe when not running
		expect(set.evictionSweepRunning).toBe(false)
		vi.advanceTimersByTime(5000)
		expect(sweep).toHaveBeenCalledTimes(3)
	})

	it("a tick actually evicts an unheld entity", () => {
		vi.useFakeTimers()
		const { set } = harness()
		set.store.absorb(makeEntity("Task", "T1"))

		set.startEvictionSweep(1000)
		vi.advanceTimersByTime(1000)
		expect(set.store.has("Task:T1")).toBe(false)
		set.stopEvictionSweep()
	})
})
