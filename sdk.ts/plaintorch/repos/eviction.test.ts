import { describe, it, expect, vi, afterEach } from "vitest"
import { EntityStore } from "../repository/entityStore"
import { makeEntity } from "../repository/test-utils"
import type { PlaintorchCoreClient } from "../coreClient"
import { plaintorchEntityPolicy } from "./policy"
import { PlaintorchRepositories } from "./repositories"

// The eviction sweep as PLAINTORCH's repositories run it: what its listings hold is kept, and the periodic
// trigger a host starts. The sweep's own mechanics are covered beside the repository set and the store.

function seed(store: EntityStore, id: string, extra: Record<string, unknown> = {}) {
	const payload = makeEntity("Objective", id, extra)
	return store.absorb(payload, Object.keys(payload))
}

describe("PlaintorchRepositories.sweep — reachable through derived views", () => {
	it("keeps entities a resolved listing holds and evicts the rest", async () => {
		const store = new EntityStore(plaintorchEntityPolicy)
		let objectiveListValue: unknown[] = []
		const client = {
			store,
			objectives: { list: async () => objectiveListValue }
		} as unknown as PlaintorchCoreClient
		const repositories = new PlaintorchRepositories(client)

		const o1 = seed(store, "O1")
		seed(store, "O2")
		expect(store.recordCount).toBe(2)

		objectiveListValue = [o1] // the listing holds O1 only
		await repositories.objectiveList.get()

		expect(repositories.sweep()).toBe(1)
		expect(store.has("Objective:O1")).toBe(true) // reachable from the listing
		expect(store.has("Objective:O2")).toBe(false) // held nowhere
	})

	it("keeps a subscribed entity that appears in no listing", async () => {
		const store = new EntityStore(plaintorchEntityPolicy)
		const client = { store, objectives: { list: async () => [] } } as unknown as PlaintorchCoreClient
		const repositories = new PlaintorchRepositories(client)

		seed(store, "O1")
		store.subscribe("Objective:O1", () => {})
		await repositories.objectiveList.get() // resolves empty — O1 is in no view

		expect(repositories.sweep()).toBe(0)
		expect(store.has("Objective:O1")).toBe(true) // kept because it is observed
	})
})

describe("periodic eviction sweep — the trigger", () => {
	afterEach(() => {
		vi.useRealTimers()
	})

	function makeRepos() {
		const store = new EntityStore(plaintorchEntityPolicy)
		const client = { store, objectives: { list: async () => [] } } as unknown as PlaintorchCoreClient
		return { repositories: new PlaintorchRepositories(client), store }
	}

	it("runs the sweep on each interval once started, and reports as running", () => {
		vi.useFakeTimers()
		const { repositories } = makeRepos()
		const sweep = vi.spyOn(repositories, "sweep")

		repositories.startEvictionSweep(1000)
		expect(repositories.evictionSweepRunning).toBe(true)

		vi.advanceTimersByTime(3000)
		expect(sweep).toHaveBeenCalledTimes(3)
		repositories.stopEvictionSweep()
	})

	it("does not sweep until the first interval elapses", () => {
		vi.useFakeTimers()
		const { repositories } = makeRepos()
		const sweep = vi.spyOn(repositories, "sweep")

		repositories.startEvictionSweep(1000)
		vi.advanceTimersByTime(999)
		expect(sweep).not.toHaveBeenCalled()
		repositories.stopEvictionSweep()
	})

	it("stops sweeping after stop", () => {
		vi.useFakeTimers()
		const { repositories } = makeRepos()
		const sweep = vi.spyOn(repositories, "sweep")

		repositories.startEvictionSweep(1000)
		vi.advanceTimersByTime(1000)
		repositories.stopEvictionSweep()
		expect(repositories.evictionSweepRunning).toBe(false)

		vi.advanceTimersByTime(5000)
		expect(sweep).toHaveBeenCalledTimes(1) // no further sweeps after stop
	})

	it("start is idempotent — a second start does not stack a second interval", () => {
		vi.useFakeTimers()
		const { repositories } = makeRepos()
		const sweep = vi.spyOn(repositories, "sweep")

		repositories.startEvictionSweep(1000)
		repositories.startEvictionSweep(1000) // ignored while one is running
		vi.advanceTimersByTime(1000)
		expect(sweep).toHaveBeenCalledTimes(1)
		repositories.stopEvictionSweep()
	})

	it("a tick actually evicts an unheld entity", () => {
		vi.useFakeTimers()
		const { repositories, store } = makeRepos()
		store.absorb(makeEntity("Objective", "O1"), ["@type", "id", "title"])
		expect(store.recordCount).toBe(1)

		repositories.startEvictionSweep(1000)
		vi.advanceTimersByTime(1000)
		expect(store.has("Objective:O1")).toBe(false)
		repositories.stopEvictionSweep()
	})
})
