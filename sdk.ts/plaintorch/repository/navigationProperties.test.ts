import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { createAbsorbingReviver } from "./absorption"
// Imported for their @model registration side effect, so the reviver constructs real instances.
import "../objectives/models"
import "../directives/models"
import "../onrush/models"

// Field-trial regressions, now fixed and guarded here: a navigation property must not vanish client-side.
//   1. Updating an objective erased its `directive` (ObjectiveBanner binds a directive item to `entity.directive`).
//   2. A directive banner lost its parent (`parentDirective`), while the grid tree — which reads the
//      `parentDirectiveId` scalar, not the nav — stayed correct.
//
// ROOT CAUSE (one gap, both bugs): `authoritativeKeys` withheld an unloaded *collection* on a nested entity but
// merged a null *single-reference* nav, and treated a *root* response as fully authoritative. A lean response
// that serialized an unloaded single-ref nav as `null` — `objectives.update` returning a base Objective,
// `directives.get` returning a DirectiveSummary aliasing the same identity — merged that `null` over the
// instance a richer listing populated.
//
// FIX: a null single-reference nav is withheld unless the same payload also nulls its `<field>Id`. An id still
// set beside a null nav is incoherent — the nav was only unloaded; a genuine clear nulls the id too. A plain
// nullable scalar (no `<field>Id` companion) is not a reference and always applies.

type Bag = Record<string, unknown>

/** Parse a whole response graph through the absorbing reviver, exactly as the client does. */
function absorb<T>(store: EntityStore, value: unknown): T {
	return JSON.parse(JSON.stringify(value), createAbsorbingReviver(store)) as T
}

const objectiveWithDirective = {
	"@type": "Objective", id: "O1", title: "Ship it", directiveId: "D1",
	directive: { "@type": "StellarDirective", id: "D1", title: "Constellation" }
}

describe("updating an objective keeps its directive nav", () => {
	it("keeps the populated directive when a lean update carries directive:null with the id still set", () => {
		const store = new EntityStore()
		// The grid's listing populated the objective WITH its directive.
		absorb(store, [objectiveWithDirective])
		expect((store.peek<Bag>("Objective:O1")!.directive as Bag)?.id).toBe("D1")

		// The update/shiftWorkflow endpoint returns a base Objective whose nav was not loaded → serialized null.
		absorb(store, { "@type": "Objective", id: "O1", title: "Ship it", status: 2, directiveId: "D1", directive: null })

		const objective = store.peek<Bag>("Objective:O1")!
		expect(objective.directiveId).toBe("D1")                              // scalar FK intact
		expect(objective.directive).toBe(store.peek("StellarDirective:D1"))  // nav survives — still the canonical D1
	})

	it("a lean response that OMITS the nav key also keeps it — omitted keys never merge", () => {
		const store = new EntityStore()
		absorb(store, [objectiveWithDirective])
		const directive = store.peek<Bag>("Objective:O1")!.directive

		absorb(store, { "@type": "Objective", id: "O1", title: "Ship it", status: 2, directiveId: "D1" })

		expect(store.peek<Bag>("Objective:O1")!.directive).toBe(directive)
	})
})

describe("a directive banner keeps its parent nav", () => {
	const childWithParent = {
		"@type": "StellarDirective", id: "D2", title: "Child", parentDirectiveId: "D1",
		parentDirective: { "@type": "StellarDirective", id: "D1", title: "Parent" }
	}

	it("keeps the populated parent when a summary carries parentDirective:null with the id still set", () => {
		const store = new EntityStore()
		// The directive listing populated the parent nav (as the grid's list does).
		absorb(store, [childWithParent])
		expect((store.peek<Bag>("StellarDirective:D2")!.parentDirective as Bag)?.id).toBe("D1")

		// The banner resolves the directive through get(id) → a summary that did not load the parent nav.
		absorb(store, { "@type": "StellarDirective", id: "D2", title: "Child", parentDirectiveId: "D1", parentDirective: null })

		const child = store.peek<Bag>("StellarDirective:D2")!
		expect(child.parentDirectiveId).toBe("D1")                                // scalar survives → the tree resolves
		expect(child.parentDirective).toBe(store.peek("StellarDirective:D1"))    // nav survives → the banner shows the parent
	})
})

describe("single-reference navs are withheld like collections when unloaded", () => {
	it("withholds a nested back-reference's null single-reference the same as its empty collection", () => {
		const store = new EntityStore()
		// D2 populated with both a single-ref (parentDirective) and a collection (subdirectives).
		absorb(store, {
			"@type": "StellarDirective", id: "D2", title: "Child",
			parentDirective: { "@type": "StellarDirective", id: "D1", title: "Parent" },
			subdirectives: [{ "@type": "StellarDirective", id: "D3", title: "Grandchild" }]
		})
		expect((store.peek<Bag>("StellarDirective:D2")!.subdirectives as unknown[]).length).toBe(1)

		// An objective drags D2 in with neither nav loaded: collection [], single-ref null, its id still set.
		absorb(store, {
			"@type": "Objective", id: "O1", title: "Ship it", directiveId: "D2",
			directive: { "@type": "StellarDirective", id: "D2", title: "Child", parentDirectiveId: "D1", parentDirective: null, subdirectives: [] }
		})

		const d2 = store.peek<Bag>("StellarDirective:D2")!
		expect((d2.subdirectives as unknown[]).length).toBe(1)             // empty collection withheld
		expect((d2.parentDirective as Bag)?.id).toBe("D1")                 // null single-ref withheld too — the gap is closed
	})
})

describe("a genuine clear still propagates — the scalar FK is the disambiguator", () => {
	// `directive: null` is ambiguous: "no directive" OR "not loaded". The FK beside it tells them apart — a
	// genuine clear nulls `directiveId` too. The fix must keep this coherent clear working while withholding
	// the incoherent unloaded case above.
	it("clears the nav when the FK is nulled in the same payload", () => {
		const store = new EntityStore()
		absorb(store, [objectiveWithDirective])
		expect((store.peek<Bag>("Objective:O1")!.directive as Bag)?.id).toBe("D1")

		absorb(store, { "@type": "Objective", id: "O1", title: "Ship it", directiveId: null, directive: null })

		const objective = store.peek<Bag>("Objective:O1")!
		expect(objective.directiveId).toBeNull() // FK cleared
		expect(objective.directive).toBeNull()   // nav cleared — a real removal still reaches the banner
	})
})

describe("ruling out the eviction hypothesis", () => {
	it("keeps a subscribed objective's directive record — the sweep closure walks single-ref navs", () => {
		const store = new EntityStore()
		absorb(store, objectiveWithDirective)
		expect(store.has("StellarDirective:D1")).toBe(true)
		store.subscribe("Objective:O1", () => {}) // a banner observes the objective, not the directive

		store.sweep() // the directive is unsubscribed and in no listing — reachable only through the objective

		expect(store.has("StellarDirective:D1")).toBe(true)                        // retained via the closure over O1
		expect((store.peek<Bag>("Objective:O1")!.directive as Bag)?.id).toBe("D1") // field untouched
	})

	it("eviction removes records but never nulls a field on an instance still held", () => {
		const store = new EntityStore()
		const objective = absorb<Bag>(store, objectiveWithDirective)
		const directive = objective.directive

		store.sweep() // nothing subscribed, no roots → both records evicted

		expect(store.has("Objective:O1")).toBe(false)
		expect(store.has("StellarDirective:D1")).toBe(false)
		expect(objective.directive).toBe(directive) // the held instance keeps its nav — eviction drops records, not fields
	})
})
