import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { createAbsorbingReviver } from "./absorption"
import { collectEntityKeys, identifyByShape } from "./identity"
import type { EntityPolicy } from "./policy"
import { makeEntity } from "./test-utils"

// A domain whose value objects carry another entity's id as a reference: an entity is a `@type`, a string `id`
// and a string `title`, so a reference — which has no title — is not one.
const identify = identifyByShape({ typeKey: "@type", requires: (value) => typeof value.title === "string" })
const policy: EntityPolicy = { identify }

// Entity eviction bounds the identity map over a long session. The overriding constraint is the
// canonical-instance guarantee: an entity a live view still holds must never be evicted, or a later fetch
// would mint a divergent second instance. The store keeps what it observes (and everything those entities
// nest); the repositories add what every resolved derived view reaches.

type Obj = { "@type": string, id: string, title: string, status?: number }

function seed(store: EntityStore, id: string, extra: Record<string, unknown> = {}) {
	const payload = makeEntity("Objective", id, extra)
	return store.absorb(payload, Object.keys(payload))
}

/** Parse a whole response graph through the absorbing reviver, so nested entities become canonical records. */
function absorbGraph<T>(store: EntityStore, value: unknown): T {
	return JSON.parse(JSON.stringify(value), createAbsorbingReviver(store)) as T
}

describe("collectEntityKeys — reachability walk", () => {
	it("collects a flat entity's key", () => {
		const into = new Set<string>()
		collectEntityKeys(makeEntity("Objective", "O1"), into, identify)
		expect([...into]).toEqual(["Objective:O1"])
	})

	it("descends through arrays and nested objects", () => {
		const view = {
			items: [makeEntity("Objective", "O1"), makeEntity("Objective", "O2")],
			nested: { deep: makeEntity("Fate", "F1") }
		}
		const into = new Set<string>()
		collectEntityKeys(view, into, identify)
		expect([...into].sort()).toEqual(["Fate:F1", "Objective:O1", "Objective:O2"])
	})

	it("terminates on a cycle between entity references", () => {
		const objective = makeEntity("Objective", "O1") as Record<string, unknown>
		const sprint = makeEntity("OnrushSprint", "N1") as Record<string, unknown>
		objective.onrushSprint = sprint
		sprint.objectives = [objective]

		const into = new Set<string>()
		collectEntityKeys(objective, into, identify)
		expect([...into].sort()).toEqual(["Objective:O1", "OnrushSprint:N1"])
	})

	it("ignores non-entities — value objects without a title, numeric-keyed records, primitives", () => {
		const into = new Set<string>()
		collectEntityKeys({ "@type": "EndpointRef", id: "O1" }, into, identify) // a reference: id but no title
		collectEntityKeys({ "@type": "Dependency", id: 5, title: "d" }, into, identify) // numeric key
		collectEntityKeys(null, into, identify)
		collectEntityKeys(42, into, identify)
		expect(into.size).toBe(0)
	})
})

describe("EntityStore.sweep — keep and evict", () => {
	it("evicts an unsubscribed entity nothing holds, leaving no index or ledger trace", () => {
		const store = new EntityStore(policy)
		seed(store, "O1")
		store.noteLocalChange("Objective:O1") // a change marker to prove it is dropped too
		expect(store.recordCount).toBe(1)

		expect(store.sweep()).toBe(1)
		expect(store.has("Objective:O1")).toBe(false)
		expect(store.entitiesOfType("Objective").length).toBe(0)
		expect(store.pendingChangeCount).toBe(0)
		expect(store.recordCount).toBe(0)
	})

	it("keeps a subscribed entity even when no view holds it", () => {
		const store = new EntityStore(policy)
		seed(store, "O1")
		store.subscribe("Objective:O1", () => {})
		expect(store.sweep()).toBe(0)
		expect(store.has("Objective:O1")).toBe(true)
	})

	it("keeps an entity a live view still reaches (external root)", () => {
		const store = new EntityStore(policy)
		seed(store, "O1")
		expect(store.sweep(new Set(["Objective:O1"]))).toBe(0)
		expect(store.has("Objective:O1")).toBe(true)
	})

	it("keeps the entities a subscribed entity nests (closure over subscribed roots)", () => {
		const store = new EntityStore(policy)
		absorbGraph(store, {
			"@type": "OnrushSprint", id: "N1", title: "s",
			checkpoints: [{ "@type": "Checkpoint", id: "C1", title: "c" }]
		})
		expect(store.has("Checkpoint:C1")).toBe(true) // nested entity became a canonical record

		store.subscribe("OnrushSprint:N1", () => {}) // only the sprint is observed
		expect(store.sweep()).toBe(0) // the checkpoint is kept because the sprint still holds it
		expect(store.has("Checkpoint:C1")).toBe(true)
	})

	it("keeps an entity with a write in flight", () => {
		const store = new EntityStore(policy)
		seed(store, "O1")
		store.beginWrite("Objective:O1")
		expect(store.sweep()).toBe(0)
		expect(store.has("Objective:O1")).toBe(true)
		store.endWrite("Objective:O1")
	})

	it("keeps a subscribed placeholder and does not choke on its undefined value", () => {
		const store = new EntityStore(policy)
		store.subscribe("Objective:O1", () => {}) // creates a placeholder, value still undefined
		expect(() => store.sweep()).not.toThrow()
		expect(store.recordCount).toBe(1) // retained — it is observed
	})

	it("evicts several at once and reports the count", () => {
		const store = new EntityStore(policy)
		seed(store, "O1")
		seed(store, "O2")
		seed(store, "O3")
		store.subscribe("Objective:O2", () => {}) // O2 observed
		expect(store.sweep(new Set(["Objective:O3"]))).toBe(1) // O3 held, O2 observed, O1 dropped
		expect(store.has("Objective:O1")).toBe(false)
		expect(store.has("Objective:O2")).toBe(true)
		expect(store.has("Objective:O3")).toBe(true)
	})
})

describe("EntityStore.sweep — canonical-instance safety", () => {
	it("keeps a held entity as the same instance; an unheld one evicts and refetches fresh", () => {
		const store = new EntityStore(policy)
		const first = seed(store, "O1", { status: 0 })

		// Held by a live view → kept, and it is the very same instance every surface shares.
		store.sweep(new Set(["Objective:O1"]))
		expect(store.peek<Obj>("Objective:O1")).toBe(first)

		// Now nothing holds it → safe to evict; a later response mints a fresh canonical instance. Had a
		// surface still held `first` across this, it would now diverge from `second` — which is exactly why
		// eviction refuses to drop anything still reachable.
		expect(store.sweep()).toBe(1)
		const second = seed(store, "O1", { status: 0 })
		expect(second).not.toBe(first)
		expect(store.peek<Obj>("Objective:O1")).toBe(second)
	})
})
