import { describe, it, expect } from "vitest"
import { createAbsorbingReviver } from "./absorption"
import { EntityStore } from "./entityStore"
import { Objective } from "../objectives/models"
import { OnrushSprint } from "../onrush/models"
import { Attentive, Decree, Eventive, Occurrence } from "../declaratives/models"

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

	it("revives occurrences into their model classes by the runtime type the core stamps", () => {
		const store = new EntityStore()
		const epoch = { "@type": "Epoch", moment: "2026-07-20T14:30:00", granularity: 6, duration: null, timeZone: null }
		const agenda = absorb<{ attentives: Attentive[], eventives: Eventive[] }>(store, {
			"@type": "PolarisAgenda",
			attentives: [{
				"@type": "Attentive", id: 0, decreeId: "R1", recurrenceOwnerUid: "R1", recurrenceId: "2026-07-20T14:30:00",
				epoch, resolution: 0, decree: { "@type": "Decree", id: "R1", title: "Standup" }
			}],
			eventives: [{
				"@type": "Eventive", id: 7, fateId: "E1", recurrenceOwnerUid: "E1", recurrenceId: "2026-07-21T00:00:00",
				epoch: { ...epoch, moment: "2026-07-21T00:00:00", granularity: 4 }, resolution: 0
			}]
		})

		const [attentive] = agenda.attentives
		const [eventive] = agenda.eventives
		expect(attentive).toBeInstanceOf(Attentive)
		expect(attentive).toBeInstanceOf(Occurrence)
		expect(eventive).toBeInstanceOf(Eventive)
		expect(eventive).toBeInstanceOf(Occurrence)
		expect(attentive!.recurrenceId).toBe("2026-07-20T14:30:00")
		expect(eventive!.recurrenceOwnerUid).toBe("E1")

		// An occurrence is a value inside its aggregate, not a tracked entity — but the decree it carries is.
		expect(attentive!.decree).toBeInstanceOf(Decree)
		expect(store.peek("Decree:R1")).toBe(attentive!.decree)
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
