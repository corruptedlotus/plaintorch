import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore.js"
import { makeEntity, testPolicy } from "./testing.js"

// `store.read` is the one call a client makes around a response-absorbing request. It opens the read before
// the request leaves, hands over the parser that absorbs under that read's revision, and closes the read
// however the request ends.

type Thing = { id: string, status: number }

const body = (status: number) => JSON.stringify(makeEntity("Thing", "1", { status }))

describe("EntityStore.read", () => {
	it("is in flight for exactly the duration of the request", async () => {
		const store = new EntityStore(testPolicy)
		let during = -1

		await store.read(async () => {
			during = store.inFlightReadCount
		})

		expect(during).toBe(1)
		expect(store.inFlightReadCount).toBe(0)
	})

	it("parses the response into the canonical instance and returns what the request returns", async () => {
		const store = new EntityStore(testPolicy)

		const thing = await store.read(async (parse) => parse<Thing>(body(3)))

		expect(thing).toBe(store.peek("Thing:1"))
		expect(thing.status).toBe(3)
		expect(await store.read(async () => "anything")).toBe("anything")
	})

	it("absorbs a list response element by element, and leaves values that are not entities as parsed", async () => {
		const store = new EntityStore(testPolicy)

		const parsed = await store.read(async (parse) =>
			parse<{ items: Thing[], total: number }>(JSON.stringify({ items: [makeEntity("Thing", "1"), makeEntity("Thing", "2")], total: 2 })))

		expect(parsed.total).toBe(2)
		expect(parsed.items[0]).toBe(store.peek("Thing:1"))
		expect(parsed.items[1]).toBe(store.peek("Thing:2"))
	})

	it("discards the response for an identity changed locally while the request was in flight", async () => {
		const store = new EntityStore(testPolicy)
		const held = store.absorb(makeEntity("Thing", "1", { status: 0 })) as unknown as Thing

		const result = await store.read(async (parse) => {
			held.status = 9
			store.touch("Thing:1")
			return parse<Thing>(body(5))
		})

		expect(result).toBe(held)
		expect(held.status).toBe(9)
	})

	it("applies an authoritative read even over such a change", async () => {
		const store = new EntityStore(testPolicy)
		const held = store.absorb(makeEntity("Thing", "1", { status: 0 })) as unknown as Thing

		await store.read(async (parse) => {
			held.status = 9
			store.touch("Thing:1")
			return parse<Thing>(body(5))
		}, { authoritative: true })

		expect(held.status).toBe(5)
	})

	it("closes the read when the request throws, and when parsing does", async () => {
		const store = new EntityStore(testPolicy)

		await expect(store.read(async () => { throw new Error("unreachable") })).rejects.toThrow("unreachable")
		await expect(store.read(async (parse) => parse("{not json"))).rejects.toThrow()

		expect(store.inFlightReadCount).toBe(0)
	})

	it("prunes the change ledger once the read settles, but not while an older read is still in flight", async () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Thing", "1", { status: 0 }))
		let releaseOlder!: () => void
		const older = store.read(async () => await new Promise<void>((resolve) => { releaseOlder = resolve }))

		store.touch("Thing:1")
		await store.read(async () => { })
		// The older read predates the change, so its response could still arrive and must still be discarded.
		expect(store.pendingChangeCount).toBe(1)

		releaseOlder()
		await older
		expect(store.pendingChangeCount).toBe(0)
	})
})
