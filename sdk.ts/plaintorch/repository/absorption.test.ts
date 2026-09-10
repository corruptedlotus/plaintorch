import { describe, it, expect } from "vitest"
import { createAbsorbingReviver } from "./absorption"
import { EntityStore } from "./entityStore"
import { Objective } from "../objectives/models"
import { OnrushSprint } from "../onrush/models"

/** Parse a response through the absorbing reviver against a store, as the client does. */
function absorb<T>(store: EntityStore, value: unknown): T {
	return JSON.parse(JSON.stringify(value), createAbsorbingReviver(store)) as T
}

describe("createAbsorbingReviver", () => {
	it("turns a list response into constructed instances, not plain objects", () => {
		const store = new EntityStore()
		const list = absorb<Objective[]>(store, [
			{ "@type": "Objective", id: "O1", title: "a" },
			{ "@type": "Objective", id: "O2", title: "b" }
		])
		expect(Array.isArray(list)).toBe(true)
		expect(list[0]).toBeInstanceOf(Objective)
		expect(store.peek("Objective:O1")).toBe(list[0]) // absorbed and canonical
	})

	it("makes a nested entity the same instance as a directly fetched one (identity map)", () => {
		const store = new EntityStore()
		const sprint = absorb<OnrushSprint>(store, {
			"@type": "OnrushSprint", id: "N1", title: "s",
			objectives: [{ "@type": "Objective", id: "O1", title: "a" }]
		})
		const nested = sprint.objectives[0]
		const direct = absorb<Objective>(store, { "@type": "Objective", id: "O1", title: "a" })
		expect(direct).toBe(nested)
	})

	it("returns non-entity nodes untouched", () => {
		const store = new EntityStore()
		const value = absorb<{ note: string, count: number }>(store, { note: "plain", count: 3 })
		expect(value).toEqual({ note: "plain", count: 3 })
	})
})

describe("authoritativeKeys — root vs nested collections", () => {
	it("applies a root's empty array (a directly fetched sprint genuinely emptied its checkpoints)", () => {
		const store = new EntityStore()
		absorb(store, { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [{ "@type": "Checkpoint", id: "C1", title: "c" }] })
		expect(store.peek<OnrushSprint>("OnrushSprint:N1")!.checkpoints.length).toBe(1)

		absorb(store, { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [] })
		expect(store.peek<OnrushSprint>("OnrushSprint:N1")!.checkpoints.length).toBe(0)
	})

	it("withholds a nested entity's empty array, so an Include back-reference cannot wipe a populated collection", () => {
		const store = new EntityStore()
		absorb(store, { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [{ "@type": "Checkpoint", id: "C1", title: "c" }] })

		// An objective dragged the sprint in as a back-reference whose checkpoints were not loaded (empty array).
		absorb(store, {
			"@type": "Objective", id: "O1", title: "a",
			onrushSprint: { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [] }
		})
		expect(store.peek<OnrushSprint>("OnrushSprint:N1")!.checkpoints.length).toBe(1) // survived
	})

	it("withholds a nested array holed with null (a cycle back-reference under IgnoreCycles)", () => {
		const store = new EntityStore()
		absorb(store, { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [{ "@type": "Checkpoint", id: "C1", title: "c" }] })

		absorb(store, {
			"@type": "Objective", id: "O1", title: "a",
			onrushSprint: { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [null] }
		})
		expect(store.peek<OnrushSprint>("OnrushSprint:N1")!.checkpoints.length).toBe(1)
	})

	it("still merges a nested entity's populated array and scalar fields", () => {
		const store = new EntityStore()
		absorb(store, { "@type": "OnrushSprint", id: "N1", title: "s", checkpoints: [] })
		absorb(store, {
			"@type": "Objective", id: "O1", title: "a",
			onrushSprint: { "@type": "OnrushSprint", id: "N1", title: "renamed", checkpoints: [{ "@type": "Checkpoint", id: "C1", title: "c" }] }
		})
		const sprint = store.peek<OnrushSprint>("OnrushSprint:N1")!
		expect(sprint.title).toBe("renamed") // scalar merged
		expect(sprint.checkpoints.length).toBe(1) // populated array merged
	})
})
