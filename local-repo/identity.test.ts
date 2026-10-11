import { describe, it, expect } from "vitest"
import { entityKey, typeNameFromKey } from "./identity.js"

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
