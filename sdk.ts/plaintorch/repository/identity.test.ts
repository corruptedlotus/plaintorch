import { describe, it, expect } from "vitest"
import { entityKey, typeNameFromKey, typeNameOf, identify, isEntity } from "./identity"

const entity = (over: Record<string, unknown> = {}) => ({ "@type": "Objective", id: "O1", title: "Ship it", ...over })

describe("entityKey / typeNameFromKey", () => {
	it("builds and splits a key", () => {
		expect(entityKey("Objective", "O1")).toBe("Objective:O1")
		expect(typeNameFromKey("Objective:O1")).toBe("Objective")
	})

	it("splits on the first colon, so an id containing a colon keeps its type", () => {
		expect(typeNameFromKey("Fate:A:weird:id")).toBe("Fate")
	})

	it("returns the whole string when there is no colon", () => {
		expect(typeNameFromKey("bare")).toBe("bare")
	})
})

describe("typeNameOf", () => {
	it("reads the stamped @type", () => {
		expect(typeNameOf(entity())).toBe("Objective")
	})

	it("is undefined for non-objects and objects without @type", () => {
		expect(typeNameOf(undefined)).toBeUndefined()
		expect(typeNameOf(null)).toBeUndefined()
		expect(typeNameOf("Objective")).toBeUndefined()
		expect(typeNameOf({ id: "O1", title: "x" })).toBeUndefined()
	})
})

describe("identify — structural recognition", () => {
	it("recognises a PUCK-named entity by @type + string id + string title", () => {
		expect(identify(entity())).toBe("Objective:O1")
		expect(isEntity(entity())).toBe(true)
	})

	it("recognises whatever @type the core emits, not a fixed list (StellarDirective drift regression)", () => {
		// The closed union once said "Directive" while the core emits "StellarDirective"; every directive
		// silently stopped being tracked. Recognition is structural, so the concrete name is honoured.
		expect(identify({ "@type": "StellarDirective", id: "A00000001", title: "North Star" })).toBe("StellarDirective:A00000001")
		expect(identify({ "@type": "LunarDirective", id: "LUNA0001", title: "Moonlight" })).toBe("LunarDirective:LUNA0001")
	})

	it("rejects a value object that references an entity but is not one (EndpointRef regression)", () => {
		// A dependency endpoint carries a string `id` pointing at another entity but has no title of its own;
		// tracking it would merge two unrelated references that happen to share an id into one instance.
		expect(identify({ "@type": "EndpointRef", id: "O1" })).toBeUndefined()
		expect(identify({ "@type": "EndpointRef", id: "O1", kind: 0 })).toBeUndefined()
		expect(isEntity({ "@type": "EndpointRef", id: "O1" })).toBe(false)
	})

	it("rejects a child record keyed on a number (Executive/Reflective/Eventive/Attentive)", () => {
		expect(identify({ "@type": "Executive", id: 42, title: "x" })).toBeUndefined()
	})

	it("rejects an empty-string id", () => {
		expect(identify(entity({ id: "" }))).toBeUndefined()
	})

	it("rejects a missing or non-string title", () => {
		expect(identify({ "@type": "Objective", id: "O1" })).toBeUndefined()
		expect(identify(entity({ title: 5 }))).toBeUndefined()
	})

	it("rejects values with no @type, and non-objects", () => {
		expect(identify({ id: "O1", title: "x" })).toBeUndefined()
		expect(identify(undefined)).toBeUndefined()
		expect(identify(null)).toBeUndefined()
		expect(identify("O1")).toBeUndefined()
	})

	it("works on a constructed instance too, since @type survives construction", () => {
		class Fake { "@type" = "Objective"; id = "O2"; title = "y" }
		expect(identify(new Fake())).toBe("Objective:O2")
	})
})
