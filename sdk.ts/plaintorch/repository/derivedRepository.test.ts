import { describe, it, expect, vi, afterEach } from "vitest"
import { EntityStore } from "./entityStore"
import { DerivedRepository, type DerivedFetcher, type DerivedRepositoryOptions } from "./derivedRepository"
import { makeEntity, testPolicy } from "./test-utils"

// A derived record is a view with no identity of its own — a listing, the briefing, a resolution — cached
// under a key the caller chooses. It shares the freshness and de-duplication machinery of an entity
// repository, and notifies only when the view actually changed.

/** A derived repository over a store of its own, for the cases where no entity is involved. */
function derived<T>(fetcher: DerivedFetcher<T>, options?: DerivedRepositoryOptions): DerivedRepository<T> {
	return new DerivedRepository<T>(new EntityStore(testPolicy), fetcher, options)
}

function counting<T>(resolve: (key: string) => T | undefined) {
	let calls = 0
	return {
		fetch: async (key: string): Promise<T | undefined> => {
			calls++
			return resolve(key)
		},
		get calls() { return calls }
	}
}

afterEach(() => {
	vi.useRealTimers()
})

describe("DerivedRepository — reads", () => {
	it("fetches a record and caches it under its key", async () => {
		const fetcher = counting((key) => `value of ${key}`)
		const repository = derived<string>(fetcher.fetch)

		expect(await repository.get("a")).toBe("value of a")
		expect(repository.peek("a")).toBe("value of a")
		expect(repository.peek("b")).toBeUndefined()
		expect(fetcher.calls).toBe(1)
	})

	it("defaults to the empty key for a single-record repository", async () => {
		const fetcher = counting(() => 42)
		const repository = derived<number>(fetcher.fetch)

		expect(await repository.get()).toBe(42)
		expect(repository.peek()).toBe(42)
		expect(repository.peek("")).toBe(42)
	})

	it("serves from cache inside the freshness window and refetches after it", async () => {
		vi.useFakeTimers()
		const fetcher = counting(() => 1)
		const repository = derived<number>(fetcher.fetch, { freshnessMs: 1_000 })

		await repository.get()
		vi.advanceTimersByTime(999)
		await repository.get()
		expect(fetcher.calls).toBe(1)
		vi.advanceTimersByTime(2)
		await repository.get()
		expect(fetcher.calls).toBe(2)
	})

	it("force and refresh bypass freshness", async () => {
		const fetcher = counting(() => 1)
		const repository = derived<number>(fetcher.fetch)

		await repository.get()
		await repository.get("", { force: true })
		await repository.refresh()
		expect(fetcher.calls).toBe(3)
	})

	it("shares one request between concurrent readers of a key, but not across keys", async () => {
		const fetcher = counting((key) => key)
		const repository = derived<string>(fetcher.fetch)

		await Promise.all([repository.get("a"), repository.get("a"), repository.get("b")])
		expect(fetcher.calls).toBe(2)
	})

	it("a failed fetch does not poison the key — the next read issues a fresh request", async () => {
		let calls = 0
		const repository = derived<number>(async () => {
			calls++
			if (calls === 1) {
				throw new Error("unreachable")
			}

			return 7
		})

		await expect(repository.get()).rejects.toThrow("unreachable")
		expect(await repository.get()).toBe(7)
	})

	it("an invalidated record is refetched on the next read", async () => {
		const fetcher = counting((key) => key)
		const repository = derived<string>(fetcher.fetch)

		await repository.get("a")
		await repository.get("b")
		repository.invalidate("a")
		await repository.get("a")
		await repository.get("b")
		expect(fetcher.calls).toBe(3)

		repository.invalidate()
		await repository.get("a")
		await repository.get("b")
		expect(fetcher.calls).toBe(5)
	})

	it("resolvedValues lists every resolved record that holds something", async () => {
		const repository = derived<string>(async (key) => key === "empty" ? undefined : key)
		repository.subscribe("never-fetched", () => { })

		await repository.get("a")
		await repository.get("b")
		await repository.get("empty")
		expect(repository.resolvedValues().sort()).toEqual(["a", "b"])
	})
})

describe("DerivedRepository — notification", () => {
	it("a subscription taken before the first fetch is notified when the record resolves", async () => {
		const repository = derived<number>(async () => 1)
		let notified = 0
		repository.subscribe("", () => notified++)

		expect(repository.hasSubscribers()).toBe(true)
		await repository.get()
		expect(notified).toBe(1)
	})

	it("re-resolving to an equivalent value notifies nobody", async () => {
		const repository = derived<{ items: number[] }>(async () => ({ items: [1, 2, 3] }))
		let notified = 0
		repository.subscribe("", () => notified++)

		await repository.get()
		await repository.refresh()
		expect(notified).toBe(1)
	})

	it("re-resolving to a changed value notifies", async () => {
		let next = [1]
		const repository = derived<number[]>(async () => next)
		let notified = 0
		repository.subscribe("", () => notified++)

		await repository.get()
		next = [1, 2]
		await repository.refresh()
		expect(notified).toBe(2)
		expect(repository.peek()).toEqual([1, 2])
	})

	it("a listing of the same canonical entities in a fresh array is equivalent — members compare by identity", async () => {
		const store = new EntityStore(testPolicy)
		const a = store.absorb(makeEntity("Thing", "a"))
		const b = store.absorb(makeEntity("Thing", "b"))
		const repository = new DerivedRepository<unknown[]>(store, async () => [a, b])
		let notified = 0
		repository.subscribe("", () => notified++)

		await repository.get()
		// A member's own field changed in place: its subscribers hear of it, the listing's membership did not change.
		;(a as Record<string, unknown>).status = 3
		await repository.refresh()
		expect(notified).toBe(1)
	})

	it("a listing whose membership changed notifies", async () => {
		const store = new EntityStore(testPolicy)
		const a = store.absorb(makeEntity("Thing", "a"))
		const b = store.absorb(makeEntity("Thing", "b"))
		let members: unknown[] = [a, b]
		const repository = new DerivedRepository<unknown[]>(store, async () => members)
		let notified = 0
		repository.subscribe("", () => notified++)

		await repository.get()
		members = [a]
		await repository.refresh()
		expect(notified).toBe(2)
	})

	it("a released subscription stops being notified and stops counting as an observer", async () => {
		let next = 1
		const repository = derived<number>(async () => next)
		let notified = 0
		const release = repository.subscribe("", () => notified++)

		await repository.get()
		release()
		next = 2
		await repository.refresh()
		expect(notified).toBe(1)
		expect(repository.hasSubscribers()).toBe(false)
	})
})

describe("DerivedRepository — revalidation", () => {
	it("revalidateIfObserved marks stale but fetches only when the record is observed", async () => {
		const fetcher = counting((key) => key)
		const repository = derived<string>(fetcher.fetch)
		await repository.get("a")
		await repository.get("b")
		repository.subscribe("b", () => { })

		await repository.revalidateIfObserved("a")
		expect(fetcher.calls).toBe(2)
		await repository.revalidateIfObserved("b")
		expect(fetcher.calls).toBe(3)

		// "a" was left stale, so its next reader pays for the fetch.
		await repository.get("a")
		expect(fetcher.calls).toBe(4)
	})

	it("revalidateObserved refetches the observed records and leaves the rest stale", async () => {
		const fetcher = counting((key) => key)
		const repository = derived<string>(fetcher.fetch)
		await repository.get("a")
		await repository.get("b")
		repository.subscribe("b", () => { })

		await repository.revalidateObserved()
		expect(fetcher.calls).toBe(3)
		await repository.get("b")
		expect(fetcher.calls).toBe(3)
		await repository.get("a")
		expect(fetcher.calls).toBe(4)
	})
})
