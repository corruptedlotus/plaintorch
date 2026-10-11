import { describe, it, expect, vi, afterEach } from "vitest"
import { objective, scriptedClient } from "./test-utils"

// The eviction sweep as PLAINTORCH's repositories run it: what its listings hold is kept, and the periodic
// trigger a host starts. The sweep's own mechanics are covered beside the repository set and the store.

afterEach(() => {
	vi.useRealTimers()
	vi.restoreAllMocks()
})

describe("PlaintorchRepositories.sweep — reachable through derived views", () => {
	it("keeps entities a resolved listing holds and evicts the rest", async () => {
		const { client } = scriptedClient({
			"/api/objectives": { body: [objective("O1")] },
			"/api/objectives/O2": { body: objective("O2") }
		})
		await client.repos.objectiveList.get()
		await client.repos.objectives.get("O2")
		expect(client.store.recordCount).toBe(2)

		expect(client.repos.sweep()).toBe(1)
		expect(client.store.has("Objective:O1")).toBe(true) // reachable from the listing
		expect(client.store.has("Objective:O2")).toBe(false) // held nowhere
	})

	it("keeps a subscribed entity that appears in no listing", async () => {
		const { client } = scriptedClient({
			"/api/objectives": { body: [] },
			"/api/objectives/O1": { body: objective("O1") }
		})
		await client.repos.objectives.get("O1")
		client.repos.objectives.subscribe("O1", () => { })
		await client.repos.objectiveList.get() // resolves empty — O1 is in no view

		expect(client.repos.sweep()).toBe(0)
		expect(client.store.has("Objective:O1")).toBe(true) // kept because it is observed
	})

	it("counts every listing PLAINTORCH declares as a root", async () => {
		const { client } = scriptedClient({
			"/api/objectives": { body: [objective("O1")] },
			"/api/fates": { body: [{ "@type": "Fate", id: "F1", title: "F1" }] },
			"/api/decrees": { body: [{ "@type": "Decree", id: "D1", title: "D1" }] },
			"/api/lorepages": { body: [{ "@type": "LorePage", id: "L1", title: "L1" }] },
			"/api/checkpoints": { body: [{ "@type": "Checkpoint", id: "C1", title: "C1" }] }
		})
		await Promise.all([
			client.repos.objectiveList.get(),
			client.repos.fateList.get(),
			client.repos.decreeList.get(),
			client.repos.loreList.get(),
			client.repos.checkpointList.get()
		])

		expect(client.store.recordCount).toBe(5)
		expect(client.repos.sweep()).toBe(0)
	})
})

describe("periodic eviction sweep — the trigger", () => {
	it("runs the sweep on each interval once started, and reports as running", () => {
		vi.useFakeTimers()
		const { client } = scriptedClient()
		const sweep = vi.spyOn(client.repos, "sweep")

		client.repos.startEvictionSweep(1000)
		expect(client.repos.evictionSweepRunning).toBe(true)

		vi.advanceTimersByTime(3000)
		expect(sweep).toHaveBeenCalledTimes(3)
		client.repos.stopEvictionSweep()
		expect(client.repos.evictionSweepRunning).toBe(false)
	})

	it("a tick actually evicts an unheld entity", async () => {
		const { client } = scriptedClient({ "/api/objectives/O1": { body: objective("O1") } })
		await client.repos.objectives.get("O1")
		vi.useFakeTimers()

		client.repos.startEvictionSweep(1000)
		vi.advanceTimersByTime(1000)
		expect(client.store.has("Objective:O1")).toBe(false)
		client.repos.stopEvictionSweep()
	})
})
