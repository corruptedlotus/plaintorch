import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { makeEntity, flushMicrotasks } from "./test-utils"

type Obj = { "@type": string, id: string, title: string, status?: number, college?: number, celestronValue?: number, tags?: string[] }
const KEY = "Objective:O1"

/** Absorbs an objective, treating exactly the given fields (plus @type/id/title) as the wire payload. */
function seed(store: EntityStore, id: string, extra: Record<string, unknown> = {}) {
	const payload = makeEntity("Objective", id, extra)
	return store.absorb(payload, Object.keys(payload))
}

describe("absorb & merge", () => {
	it("adopts a new entity, then serves it from peek", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		expect(store.has(KEY)).toBe(true)
		expect(store.peek<Obj>(KEY)!.id).toBe("O1")
	})

	it("merges only the keys the payload carried — a sparse response never erases richer cached fields", () => {
		const store = new EntityStore()
		store.absorb(makeEntity("Objective", "O1", { title: "Original", status: 0, college: 3, celestronValue: 5 }),
			["@type", "id", "title", "status", "college", "celestronValue"])

		// A later sparse response carries only status.
		store.absorb(makeEntity("Objective", "O1", { title: "Original", status: 2 }), ["@type", "id", "title", "status"])

		const objective = store.peek<Obj>(KEY)!
		expect(objective.status).toBe(2) // updated
		expect(objective.college).toBe(3) // survived — not in the sparse payload's keys
		expect(objective.celestronValue).toBe(5)
	})

	it("keeps the same instance across merges (references stay stable for surfaces holding it)", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const first = store.peek<Obj>(KEY)
		store.absorb(makeEntity("Objective", "O1", { status: 4 }), ["@type", "id", "title", "status"])
		expect(store.peek<Obj>(KEY)).toBe(first)
	})

	it("does not bump the version or notify when nothing actually changed", () => {
		const store = new EntityStore()
		store.absorb(makeEntity("Objective", "O1", { title: "Original", status: 2 }), ["@type", "id", "title", "status"])
		const version = store.version(KEY)
		let fired = 0
		store.subscribe(KEY, () => fired++)
		store.absorb(makeEntity("Objective", "O1", { title: "Original", status: 2 }), ["@type", "id", "title", "status"])
		expect(store.version(KEY)).toBe(version)
		expect(fired).toBe(0)
	})

	it("returns a non-entity untouched", () => {
		const store = new EntityStore()
		const value = { id: "not-an-entity", title: "x" }
		expect(store.absorb(value)).toBe(value)
	})
})

describe("supersede ordering", () => {
	it("discards a response issued before a local change to the same identity", () => {
		const store = new EntityStore()
		seed(store, "S1", { status: 0 })
		const issuedAt = store.currentRevision
		store.noteLocalChange("Objective:S1") // a local edit happens after the read went out

		store.absorb(makeEntity("Objective", "S1", { status: 9 }), ["@type", "id", "title", "status"], { issuedAt })
		expect(store.peek<Obj>("Objective:S1")!.status).toBe(0) // the stale response was dropped
	})

	it("applies an authoritative response even over a local change", () => {
		const store = new EntityStore()
		seed(store, "S1", { status: 0 })
		const issuedAt = store.currentRevision
		store.noteLocalChange("Objective:S1")

		store.absorb(makeEntity("Objective", "S1", { status: 9 }), ["@type", "id", "title", "status"], { issuedAt, authoritative: true })
		expect(store.peek<Obj>("Objective:S1")!.status).toBe(9)
	})

	it("acceptAuthority gives up the local claim so the next read applies whatever its age", () => {
		const store = new EntityStore()
		seed(store, "S2", { status: 0 })
		const issuedAt = store.currentRevision
		store.noteLocalChange("Objective:S2")
		store.acceptAuthority("Objective:S2")

		store.absorb(makeEntity("Objective", "S2", { status: 7 }), ["@type", "id", "title", "status"], { issuedAt })
		expect(store.peek<Obj>("Objective:S2")!.status).toBe(7)
	})

	it("without an issuedAt, a response is never treated as superseded", () => {
		const store = new EntityStore()
		seed(store, "S3", { status: 0 })
		store.noteLocalChange("Objective:S3")
		store.absorb(makeEntity("Objective", "S3", { status: 5 }), ["@type", "id", "title", "status"])
		expect(store.peek<Obj>("Objective:S3")!.status).toBe(5)
	})
})

describe("subscriptions", () => {
	it("fires a per-identity subscriber synchronously on an in-place edit", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		let fired = 0
		store.subscribe(KEY, () => fired++)
		store.touch(KEY)
		expect(fired).toBe(1) // synchronous, no await
	})

	it("lets a component subscribe before the entity is absorbed (placeholder), then fires on first absorb", () => {
		const store = new EntityStore()
		let fired = 0
		store.subscribe(KEY, () => fired++) // creates a placeholder
		expect(store.peek(KEY)).toBeUndefined()
		seed(store, "O1", { status: 0 })
		expect(fired).toBe(1)
		expect(store.peek<Obj>(KEY)!.id).toBe("O1")
	})

	it("releases a per-identity subscription", () => {
		const store = new EntityStore()
		seed(store, "O1")
		let fired = 0
		const release = store.subscribe(KEY, () => fired++)
		release()
		store.touch(KEY)
		expect(fired).toBe(0)
	})

	it("coalesces a burst into one store-wide notification", async () => {
		const store = new EntityStore()
		seed(store, "O1")
		let global = 0
		store.subscribeAll(() => global++)
		store.touch(KEY)
		seed(store, "O2") // a second change in the same tick
		await flushMicrotasks()
		expect(global).toBe(1)
	})

	it("notifies a type subscriber for its own type only", async () => {
		const store = new EntityStore()
		seed(store, "O1")
		store.absorb(makeEntity("Fate", "F1"), ["@type", "id", "title"])
		let objective = 0
		let fate = 0
		store.subscribeType("Objective", () => objective++)
		store.subscribeType("Fate", () => fate++)

		store.touch(KEY)
		seed(store, "O2") // membership change also counts as an Objective change
		await flushMicrotasks()
		expect(objective).toBe(1)
		expect(fate).toBe(0)

		store.touch("Fate:F1")
		await flushMicrotasks()
		expect(fate).toBe(1)
		expect(objective).toBe(1) // unchanged — a Fate change does not re-run the Objective subscriber
	})

	it("enumerates a type's members through the index", () => {
		const store = new EntityStore()
		seed(store, "O1")
		seed(store, "O2")
		store.absorb(makeEntity("Fate", "F1"), ["@type", "id", "title"])
		expect(store.entitiesOfType("Objective").length).toBe(2)
		expect(store.entitiesOfType("Fate").length).toBe(1)
		expect(store.entitiesOfType("PolarisCycle").length).toBe(0)
	})

	it("schedules nothing when no structural observer exists", async () => {
		const store = new EntityStore()
		seed(store, "O1")
		// No subscribeAll / subscribeType — a change must not throw and entitiesOfType still works.
		store.touch(KEY)
		await flushMicrotasks()
		expect(store.entitiesOfType("Objective").length).toBe(1)
	})
})

describe("in-place edits", () => {
	it("patch merges a change and notifies", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		let fired = 0
		store.subscribe(KEY, () => fired++)
		store.patch(KEY, { status: 3 })
		expect(store.peek<Obj>(KEY)!.status).toBe(3)
		expect(fired).toBe(1)
	})

	it("restore reverts to a snapshot", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const snapshot = store.snapshot(KEY)!
		store.patch(KEY, { status: 9 })
		store.restore(KEY, snapshot)
		expect(store.peek<Obj>(KEY)!.status).toBe(0)
	})

	it("snapshot is shallow — nested collections are shared with the live instance (documented limitation)", () => {
		const store = new EntityStore()
		seed(store, "O1", { tags: ["a"] })
		const snapshot = store.snapshot(KEY)!
		expect(snapshot.tags).toBe(store.peek<Obj>(KEY)!.tags)
	})
})

describe("write guard & revision", () => {
	it("marks a write in flight and notes a local change at both ends", () => {
		const store = new EntityStore()
		seed(store, "O1")
		const start = store.currentRevision
		store.beginWrite(KEY)
		expect(store.isWriting(KEY)).toBe(true)
		expect(store.currentRevision).toBeGreaterThan(start)
		const mid = store.currentRevision
		store.endWrite(KEY)
		expect(store.isWriting(KEY)).toBe(false)
		expect(store.currentRevision).toBeGreaterThan(mid)
	})
})

describe("staleness", () => {
	it("markStale then absorb clears staleness", () => {
		const store = new EntityStore()
		seed(store, "O1")
		store.markStale(KEY)
		expect(store.isStale(KEY)).toBe(true)
		seed(store, "O1")
		expect(store.isStale(KEY)).toBe(false)
	})

	it("markAllStale marks every held identity", () => {
		const store = new EntityStore()
		seed(store, "O1")
		seed(store, "O2")
		store.markAllStale()
		expect(store.isStale("Objective:O1")).toBe(true)
		expect(store.isStale("Objective:O2")).toBe(true)
	})

	it("an unknown identity reads as stale", () => {
		const store = new EntityStore()
		expect(store.isStale("Objective:nope")).toBe(true)
	})
})
