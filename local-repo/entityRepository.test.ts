import { describe, it, expect, vi, afterEach } from "vitest"
import { EntityStore } from "./entityStore.js"
import { EntityRepository } from "./entityRepository.js"
import type { InvalidationScheduler } from "./invalidation.js"
import { makeEntity, testPolicy } from "./testing.js"

// The repository owns *when the core is contacted*: the freshness window, in-flight de-duplication, and the
// write paths. The store beneath it is real; the fetcher stands in for the client, absorbing what it returns
// the way a response does on its way through the reviver.

type Thing = { "@type": string, id: string, title: string, status?: number }

function harness(options: { freshnessMs?: number, resolve?: (id: string) => Record<string, unknown> | undefined } = {}) {
	const store = new EntityStore(testPolicy)
	const invalidated: string[] = []
	const invalidation = {
		invalidate: (typeName: string, id: string) => { invalidated.push(`${typeName}:${id}`) }
	} as unknown as InvalidationScheduler
	let calls = 0
	const resolve = options.resolve ?? ((id: string) => makeEntity("Thing", id, { status: 0 }))
	const repository = new EntityRepository<Thing>(
		store,
		"Thing",
		async (id) => {
			calls++
			const payload = resolve(id)
			return payload ? store.absorb(payload, Object.keys(payload)) as Thing : undefined
		},
		{ freshnessMs: options.freshnessMs, invalidation }
	)

	return {
		store,
		repository,
		invalidated,
		get calls() { return calls }
	}
}

afterEach(() => {
	vi.useRealTimers()
})

describe("EntityRepository — reads", () => {
	it("fetches an unknown entity and returns the canonical instance", async () => {
		const h = harness()
		const thing = await h.repository.get("1")
		expect(h.calls).toBe(1)
		expect(thing).toBe(h.store.peek("Thing:1"))
		expect(h.repository.peek("1")).toBe(thing)
	})

	it("serves from the store inside the freshness window without contacting the core", async () => {
		const h = harness()
		const first = await h.repository.get("1")
		const second = await h.repository.get("1")
		expect(h.calls).toBe(1)
		expect(second).toBe(first)
	})

	it("refetches once the freshness window has elapsed", async () => {
		vi.useFakeTimers()
		const h = harness({ freshnessMs: 1_000 })
		await h.repository.get("1")
		vi.advanceTimersByTime(999)
		await h.repository.get("1")
		expect(h.calls).toBe(1)
		vi.advanceTimersByTime(2)
		await h.repository.get("1")
		expect(h.calls).toBe(2)
	})

	it("refetches an invalidated entity on the next read", async () => {
		const h = harness()
		await h.repository.get("1")
		h.repository.invalidate("1")
		await h.repository.get("1")
		expect(h.calls).toBe(2)
	})

	it("refetches when the store marks the identity stale behind the repository's back", async () => {
		const h = harness()
		await h.repository.get("1")
		h.store.markStale("Thing:1")
		await h.repository.get("1")
		expect(h.calls).toBe(2)
	})

	it("force and refresh bypass freshness", async () => {
		const h = harness()
		await h.repository.get("1")
		await h.repository.get("1", { force: true })
		await h.repository.refresh("1")
		expect(h.calls).toBe(3)
	})

	it("keeps the same instance across a refetch that changed a field", async () => {
		let status = 0
		const h = harness({ resolve: (id) => makeEntity("Thing", id, { status }) })
		const first = await h.repository.get("1")
		status = 4
		const second = await h.repository.refresh("1")
		expect(second).toBe(first)
		expect(first?.status).toBe(4)
	})

	it("shares one request between concurrent readers of the same identity", async () => {
		const h = harness()
		const [a, b] = await Promise.all([h.repository.get("1"), h.repository.get("1")])
		expect(h.calls).toBe(1)
		expect(a).toBe(b)
	})

	it("a forced read joins a request already in flight rather than issuing a second", async () => {
		const h = harness()
		await Promise.all([h.repository.get("1"), h.repository.get("1", { force: true })])
		expect(h.calls).toBe(1)
	})

	it("does not share a request between different identities", async () => {
		const h = harness()
		await Promise.all([h.repository.get("1"), h.repository.get("2")])
		expect(h.calls).toBe(2)
	})

	it("resolves to nothing when the fetch finds nothing, and asks again next time", async () => {
		const h = harness({ resolve: () => undefined })
		expect(await h.repository.get("1")).toBeUndefined()
		expect(await h.repository.get("1")).toBeUndefined()
		expect(h.calls).toBe(2)
	})

	it("a failed fetch does not poison the identity — the next read issues a fresh request", async () => {
		const store = new EntityStore(testPolicy)
		let calls = 0
		const repository = new EntityRepository<Thing>(store, "Thing", async (id) => {
			calls++
			if (calls === 1) {
				throw new Error("unreachable")
			}

			return store.absorb(makeEntity("Thing", id)) as Thing
		})

		await expect(repository.get("1")).rejects.toThrow("unreachable")
		expect(await repository.get("1")).toBeDefined()
		expect(calls).toBe(2)
	})
})

describe("EntityRepository — revalidation", () => {
	it("revalidateIfObserved marks stale but fetches nothing when nobody observes the identity", async () => {
		const h = harness()
		await h.repository.get("1")
		await h.repository.revalidateIfObserved("1")
		expect(h.calls).toBe(1)
		expect(h.store.isStale("Thing:1")).toBe(true)
	})

	it("revalidateIfObserved refetches an observed identity", async () => {
		const h = harness()
		await h.repository.get("1")
		h.repository.subscribe("1", () => { })
		await h.repository.revalidateIfObserved("1")
		expect(h.calls).toBe(2)
		expect(h.store.isStale("Thing:1")).toBe(false)
	})

	it("revalidateIfObserved leaves an identity with a write in flight alone", async () => {
		const h = harness()
		await h.repository.get("1")
		h.repository.subscribe("1", () => { })
		h.store.beginWrite("Thing:1")
		await h.repository.revalidateIfObserved("1")
		expect(h.calls).toBe(1)
		h.store.endWrite("Thing:1")
	})

	it("revalidateObserved refetches only the resolved identities something observes", async () => {
		const h = harness()
		await h.repository.get("1")
		await h.repository.get("2")
		h.repository.subscribe("2", () => { })
		await h.repository.revalidateObserved()
		expect(h.calls).toBe(3)
	})

	it("a released subscription no longer counts as observing", async () => {
		const h = harness()
		await h.repository.get("1")
		const release = h.repository.subscribe("1", () => { })
		release()
		await h.repository.revalidateIfObserved("1")
		expect(h.calls).toBe(1)
	})
})

describe("EntityRepository — writes", () => {
	it("mutate holds the identity for the operation, returns its result and invalidates on success", async () => {
		const h = harness()
		await h.repository.get("1")
		let heldDuring = false
		const result = await h.repository.mutate("1", async () => {
			heldDuring = h.repository.isMutating("1")
			return { saved: true }
		})

		expect(result).toEqual({ saved: true })
		expect(heldDuring).toBe(true)
		expect(h.repository.isMutating("1")).toBe(false)
		expect(h.invalidated).toEqual(["Thing:1"])
	})

	it("mutate rolls a rejected write back to the snapshot and invalidates nothing", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const snapshot = h.repository.snapshot("1")
		thing.status = 9

		const result = await h.repository.mutate("1", async () => undefined, { rollbackTo: snapshot })

		expect(result).toBeUndefined()
		expect(thing.status).toBe(0)
		expect(h.invalidated).toEqual([])
	})

	it("mutate honours a custom success predicate", async () => {
		const h = harness()
		await h.repository.get("1")
		await h.repository.mutate("1", async () => undefined, { succeeded: () => true })
		expect(h.invalidated).toEqual(["Thing:1"])
	})

	it("mutate rolls back and rethrows when the operation throws, releasing the hold", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const snapshot = h.repository.snapshot("1")
		thing.status = 9

		await expect(h.repository.mutate("1", async () => { throw new Error("boom") }, { rollbackTo: snapshot }))
			.rejects.toThrow("boom")
		expect(thing.status).toBe(0)
		expect(h.repository.isMutating("1")).toBe(false)
	})

	it("a successful mutate counts as a fresh resolution — an expired entity is served without a fetch", async () => {
		vi.useFakeTimers()
		const h = harness({ freshnessMs: 1_000 })
		await h.repository.get("1")
		vi.advanceTimersByTime(5_000)
		await h.repository.mutate("1", async () => true)
		await h.repository.get("1")
		expect(h.calls).toBe(1)
	})

	it("commit reports acceptance as a boolean and invalidates", async () => {
		const h = harness()
		await h.repository.get("1")
		expect(await h.repository.commit("1", async () => ({ ok: 1 }))).toBe(true)
		expect(h.invalidated).toEqual(["Thing:1"])
	})

	it("commit rolls back a rejected edit already applied to the canonical instance", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const snapshot = h.repository.snapshot("1")
		thing.status = 9

		expect(await h.repository.commit("1", async () => undefined, snapshot)).toBe(false)
		expect(thing.status).toBe(0)
		expect(h.invalidated).toEqual([])
	})

	it("touch announces an in-place edit to the identity's subscribers", async () => {
		const h = harness()
		await h.repository.get("1")
		let notified = 0
		h.repository.subscribe("1", () => notified++)
		h.repository.touch("1")
		expect(notified).toBe(1)
	})

	it("rollback restores a snapshot and ignores an absent one", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const snapshot = h.repository.snapshot("1")
		thing.status = 9
		h.repository.rollback("1", undefined)
		expect(thing.status).toBe(9)
		h.repository.rollback("1", snapshot)
		expect(thing.status).toBe(0)
	})
})

describe("EntityRepository — fork", () => {
	it("is undefined until the entity has been resolved into the store", () => {
		const h = harness()
		expect(h.repository.fork("1")).toBeUndefined()
	})

	it("opens a draft whose edits stay off the canonical instance until commit", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const draft = h.repository.fork("1")!

		draft.value.status = 7
		expect(draft.value).not.toBe(thing)
		expect(thing.status).toBe(0)
		expect(draft.dirty).toBe(true)
		expect(draft.diff()).toEqual({ status: 7 })

		const sent: unknown[] = []
		expect(await draft.commit(async (patch) => { sent.push(patch); return true })).toBe(true)
		expect(thing.status).toBe(7)
		expect(sent).toEqual([{ status: 7 }])
		expect(h.invalidated).toEqual(["Thing:1"])
	})

	it("a rejected draft commit restores the canonical instance", async () => {
		const h = harness()
		const thing = (await h.repository.get("1"))!
		const draft = h.repository.fork("1")!
		draft.value.status = 7

		expect(await draft.commit(async () => undefined)).toBe(false)
		expect(thing.status).toBe(0)
	})
})
