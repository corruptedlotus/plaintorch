import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { createAbsorbingReviver } from "./absorption"
// Imported for their @model registration side effect, so the reviver constructs real instances.
import "../objectives/models"
import "../directives/models"

// Issue B (field trial): editing an objective via the banner threw while serialising the update body.
//
// The identity map cross-links objective.directive.objectives[…] back to the objective, so a canonical entity
// is a cyclic graph by construction — that is not a bug, it is the whole point of the map. The bug was the
// binder's catch-all sending the WHOLE entity as the update body ('*': (entity) => update(id, entity)), which
// the transport JSON.stringify's — and a cyclic body throws. The fix: the binder's '*' now sends only the
// changed field ({ [keyPath]: value }), which has no cycle. These tests guard that constraint: the whole entity
// must never be a write body; a per-field partial is what writes carry.

type Bag = Record<string, unknown>

function absorb<T>(store: EntityStore, value: unknown): T {
	return JSON.parse(JSON.stringify(value), createAbsorbingReviver(store)) as T
}

describe("objective update — cyclic serialization of the whole entity", () => {
	it("the canonical objective is a cyclic graph and cannot be a write body — why writes send partials", () => {
		const store = new EntityStore()
		// A response cross-linking objective <-> directive, as the identity map does once both sides are seen.
		absorb(store, {
			"@type": "Objective", id: "O1", title: "Ship it", directiveId: "D1",
			directive: {
				"@type": "StellarDirective", id: "D1", title: "Constellation",
				objectives: [{ "@type": "Objective", id: "O1", title: "Ship it" }]
			}
		})

		const objective = store.peek<Bag>("Objective:O1")!
		// The cross-link is real: objective.directive.objectives[0] is the objective itself.
		expect(((objective.directive as Bag).objectives as Bag[])[0]).toBe(objective)

		// core.objectives.update(id, entity) -> transport JSON.stringify(entity) blows up on the cycle.
		expect(() => JSON.stringify(objective)).toThrow(/circular/i)
	})

	it("a partial update body (only the changed field) has no cycle — the direction a fix takes", () => {
		const store = new EntityStore()
		absorb(store, {
			"@type": "Objective", id: "O1", title: "Ship it", directiveId: "D1",
			directive: {
				"@type": "StellarDirective", id: "D1", title: "Constellation",
				objectives: [{ "@type": "Objective", id: "O1", title: "Ship it" }]
			}
		})
		const objective = store.peek<Bag>("Objective:O1")!

		// What a per-field or diff send would carry — a scalar patch, cycle-free.
		expect(() => JSON.stringify({ college: objective.college })).not.toThrow()
	})
})
