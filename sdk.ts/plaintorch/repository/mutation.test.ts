import { describe, it, expect } from "vitest"
import { isSuccessfulMutation, runWrite } from "./mutation"
import { EntityStore } from "./entityStore"
import type { InvalidationScheduler } from "./invalidation"
import { makeEntity } from "./test-utils"

describe("isSuccessfulMutation", () => {
	it("treats an absent result — undefined, null, false — as a rejection", () => {
		expect(isSuccessfulMutation(undefined)).toBe(false)
		expect(isSuccessfulMutation(null)).toBe(false)
		expect(isSuccessfulMutation(false)).toBe(false)
	})

	it("treats any present result as success, including 0, empty string, and objects", () => {
		expect(isSuccessfulMutation(0)).toBe(true)
		expect(isSuccessfulMutation("")).toBe(true)
		expect(isSuccessfulMutation(true)).toBe(true)
		expect(isSuccessfulMutation({})).toBe(true)
	})
})

function fixture() {
	const store = new EntityStore()
	store.absorb(makeEntity("Objective", "O1", { title: "Original", status: 0 }), ["@type", "id", "title", "status"])
	const key = "Objective:O1"
	const invalidated: Array<[string, string]> = []
	const invalidation = { invalidate: (t: string, id: string) => invalidated.push([t, id]) } as unknown as InvalidationScheduler
	return { store, key, invalidated, invalidation }
}

describe("runWrite — the shared write cycle", () => {
	it("holds the write guard for the operation and releases it after", async () => {
		const { store, key, invalidation } = fixture()
		let heldDuring = false
		await runWrite(store, invalidation, "Objective", "O1", async () => {
			heldDuring = store.isWriting(key)
			return {}
		})
		expect(heldDuring).toBe(true)
		expect(store.isWriting(key)).toBe(false)
	})

	it("invalidates on success and does not roll back", async () => {
		const { store, key, invalidated, invalidation } = fixture()
		store.peek<{ title: string }>(key)!.title = "Edited"
		const before = store.snapshot(key)
		const { ok } = await runWrite(store, invalidation, "Objective", "O1", async () => ({ ok: true }), { rollbackTo: before })
		expect(ok).toBe(true)
		expect(invalidated).toEqual([["Objective", "O1"]])
		expect(store.peek<{ title: string }>(key)!.title).toBe("Edited")
	})

	it("rolls back a rejected write and does not invalidate", async () => {
		const { store, key, invalidated, invalidation } = fixture()
		const before = store.snapshot(key)!
		store.peek<{ title: string }>(key)!.title = "WillRevert"
		const { ok } = await runWrite(store, invalidation, "Objective", "O1", async () => false, { rollbackTo: before })
		expect(ok).toBe(false)
		expect(invalidated).toEqual([])
		expect(store.peek<{ title: string }>(key)!.title).toBe("Original")
	})

	it("rolls back and rethrows when the operation throws, still releasing the guard", async () => {
		const { store, key, invalidation } = fixture()
		const before = store.snapshot(key)!
		store.peek<{ title: string }>(key)!.title = "WillRevert"
		await expect(runWrite(store, invalidation, "Objective", "O1", async () => { throw new Error("boom") }, { rollbackTo: before }))
			.rejects.toThrow("boom")
		expect(store.peek<{ title: string }>(key)!.title).toBe("Original")
		expect(store.isWriting(key)).toBe(false)
	})

	it("honours a custom success predicate", async () => {
		const { store, key, invalidated, invalidation } = fixture()
		const before = store.snapshot(key)!
		store.peek<{ title: string }>(key)!.title = "Edited"
		// The default would call `{ code: 1 }` a success; the custom predicate says otherwise → rollback.
		const { ok } = await runWrite(store, invalidation, "Objective", "O1", async () => ({ code: 1 }),
			{ rollbackTo: before, succeeded: (r: { code: number }) => r.code === 0 })
		expect(ok).toBe(false)
		expect(invalidated).toEqual([])
		expect(store.peek<{ title: string }>(key)!.title).toBe("Original")
	})
})
