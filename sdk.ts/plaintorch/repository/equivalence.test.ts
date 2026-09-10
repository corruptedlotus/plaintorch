import { describe, it, expect } from "vitest"
import { isEquivalent } from "./equivalence"

describe("isEquivalent — base", () => {
	it("compares primitives and identical references", () => {
		expect(isEquivalent(1, 1)).toBe(true)
		expect(isEquivalent("a", "a")).toBe(true)
		expect(isEquivalent(1, 2)).toBe(false)
		const shared = {}
		expect(isEquivalent(shared, shared)).toBe(true)
	})

	it("deep-compares plain objects and arrays", () => {
		expect(isEquivalent({ a: 1, b: [1, 2] }, { a: 1, b: [1, 2] })).toBe(true)
		expect(isEquivalent({ a: 1 }, { a: 2 })).toBe(false)
		expect(isEquivalent({ a: 1 }, { a: 1, b: 2 })).toBe(false)
		expect(isEquivalent([1, 2], [1, 2, 3])).toBe(false)
	})

	it("null / undefined handling", () => {
		expect(isEquivalent(null, null)).toBe(true)
		expect(isEquivalent(undefined, undefined)).toBe(true)
		expect(isEquivalent(null, undefined)).toBe(false)
		expect(isEquivalent(null, {})).toBe(false)
	})
})

describe("isEquivalent — entities short-circuit on identity", () => {
	it("treats two instances of one identity as equivalent, without comparing fields", () => {
		// The store has already reconciled them and they notify their own observers, so a differing field is
		// irrelevant here — what matters is that a refetch of the same identity is not seen as a change.
		const a = { "@type": "Objective", id: "O1", title: "x", status: 0 }
		const b = { "@type": "Objective", id: "O1", title: "DIFFERENT", status: 9 }
		expect(isEquivalent(a, b)).toBe(true)
	})

	it("treats different identities as not equivalent", () => {
		const a = { "@type": "Objective", id: "O1", title: "x" }
		const c = { "@type": "Objective", id: "O2", title: "x" }
		expect(isEquivalent(a, c)).toBe(false)
	})

	it("an entity and a non-entity are never equivalent", () => {
		const e = { "@type": "Objective", id: "O1", title: "x" }
		expect(isEquivalent(e, { id: "O1", title: "x" })).toBe(false)
	})

	it("terminates when a cycle runs through an entity reference", () => {
		const a = { "@type": "Objective", id: "A", title: "a" } as Record<string, unknown>
		a.self = a
		const b = { "@type": "Objective", id: "A", title: "a" } as Record<string, unknown>
		b.self = b
		// Recurse into the plain wrapper, hit entity A, short-circuit on identity — no infinite descent.
		expect(() => isEquivalent({ node: a }, { node: b })).not.toThrow()
		expect(isEquivalent({ node: a }, { node: b })).toBe(true)
	})
})

describe("isEquivalent — depth cap (documented boundary)", () => {
	it("gives up past its depth cap, so a very deep plain structure compares unequal even when it matches", () => {
		const deep = (n: number): Record<string, unknown> => (n === 0 ? { v: 1 } : { next: deep(n - 1) })
		expect(isEquivalent(deep(3), deep(3))).toBe(true)
		expect(isEquivalent(deep(10), deep(10))).toBe(false)
	})
})
