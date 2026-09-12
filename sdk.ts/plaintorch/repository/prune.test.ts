import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { runWrite } from "./mutation"
import type { InvalidationScheduler } from "./invalidation"
import { makeEntity } from "./test-utils"

// The change ledger (`changedAt`) grows one entry per locally edited identity and, before this, only ever
// shrank when the core declared a change authoritative — so a long session leaked. The prune bounds it: a
// marker at revision r only discards responses *issued before* r, so once no read still in flight was issued
// before r, the marker can never drop anything again and is dropped. `beginRead`/`endRead` bracket every
// response-absorbing request so the store knows the oldest response that could still arrive.
//
// The danger these tests guard is the one the marker exists to prevent: pruning a marker while a slower read
// issued before the edit is still out would let that read's stale response revert the edit on arrival.

type Obj = { "@type": string, id: string, title: string, status?: number, college?: number }
const KEY = "Objective:O1"
const FIELDS = ["@type", "id", "title", "status"]

function seed(store: EntityStore, id: string, extra: Record<string, unknown> = {}) {
	const payload = makeEntity("Objective", id, extra)
	return store.absorb(payload, Object.keys(payload))
}

/** A late server response for an objective, absorbed as if it were issued at `issuedAt`. */
function respond(store: EntityStore, id: string, status: number, issuedAt: number) {
	store.absorb(makeEntity("Objective", id, { status }), FIELDS, { issuedAt })
}

describe("beginRead / endRead — tracking reads in flight", () => {
	it("stamps a read with the current revision, like currentRevision did", () => {
		const store = new EntityStore()
		const before = store.currentRevision
		expect(store.beginRead()).toBe(before)
		store.endRead(before)

		store.noteLocalChange("Objective:X")
		const after = store.currentRevision
		expect(store.beginRead()).toBe(after)
		store.endRead(after)
	})

	it("tracks concurrent reads at the same revision as a multiset, not a set", () => {
		const store = new EntityStore()
		const a = store.beginRead()
		const b = store.beginRead()
		expect(a).toBe(b) // same revision — no edits between them
		expect(store.inFlightReadCount).toBe(2)

		store.endRead(a)
		expect(store.inFlightReadCount).toBe(1) // b is still out
		store.endRead(b)
		expect(store.inFlightReadCount).toBe(0)
	})

	it("ignores an endRead for a revision it never opened", () => {
		const store = new EntityStore()
		expect(() => store.endRead(999)).not.toThrow()
		expect(store.inFlightReadCount).toBe(0)
	})
})

describe("prune — bounding the change ledger", () => {
	it("clears the whole ledger once the last read settles with nothing else in flight", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		seed(store, "O2", { status: 0 })
		seed(store, "O3", { status: 0 })
		const read = store.beginRead() // issued before the burst

		store.patch("Objective:O1", { status: 1 })
		store.patch("Objective:O2", { status: 2 })
		store.patch("Objective:O3", { status: 3 })
		expect(store.pendingChangeCount).toBe(3)

		store.endRead(read) // nothing older remains → every marker is spent
		expect(store.pendingChangeCount).toBe(0)
		expect(store.inFlightReadCount).toBe(0)
	})

	it("is read-driven: a local change lingers until some read settles, then is swept", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		store.patch(KEY, { status: 5 })
		expect(store.pendingChangeCount).toBe(1) // no read has settled yet, so nothing pruned

		const read = store.beginRead()
		store.endRead(read) // an unrelated read cycle flushes the spent marker
		expect(store.pendingChangeCount).toBe(0)
	})

	it("prunes only markers at or below the oldest read still in flight", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const readA = store.beginRead() // revision 0
		store.patch(KEY, { status: 5 }) // revision 1 — the marker
		const readB = store.beginRead() // revision 1

		expect(store.pendingChangeCount).toBe(1)
		store.endRead(readB) // readA (revision 0) still predates the edit → marker retained
		expect(store.pendingChangeCount).toBe(1)
		store.endRead(readA) // now nothing predates it
		expect(store.pendingChangeCount).toBe(0)
	})
})

describe("prune safety — never strands an edit a slower read could clobber", () => {
	it("keeps the marker while an older read is in flight, so that read's stale response is dropped", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const stale = store.beginRead() // an aggregate/list read leaves, carrying pre-edit state
		store.patch(KEY, { status: 5 }) // the user edits after it went out

		expect(store.pendingChangeCount).toBe(1) // marker held — the read has not settled

		respond(store, "O1", 0, stale) // the stale response finally arrives
		expect(store.peek<Obj>(KEY)!.status).toBe(5) // dropped, not reverted

		store.endRead(stale)
		expect(store.pendingChangeCount).toBe(0) // now spent
	})

	it("holds a marker until the LAST read at the oldest revision settles (multiset is load-bearing)", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const a = store.beginRead() // revision 0
		const b = store.beginRead() // revision 0 — a second reader at the same point
		store.patch(KEY, { status: 5 })

		store.endRead(a)
		expect(store.pendingChangeCount).toBe(1) // b (revision 0) still predates the edit

		respond(store, "O1", 0, b) // b's stale response must still be dropped
		expect(store.peek<Obj>(KEY)!.status).toBe(5)

		store.endRead(b)
		expect(store.pendingChangeCount).toBe(0)
	})

	it("once pruned, a genuinely newer read applies normally", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const first = store.beginRead()
		store.patch(KEY, { status: 5 })
		store.endRead(first) // ledger clears

		const fresh = store.beginRead() // issued now, after the edit
		respond(store, "O1", 8, fresh) // server's newer value
		expect(store.peek<Obj>(KEY)!.status).toBe(8) // applied — no stale marker in the way
		store.endRead(fresh)
	})
})

describe("versions clashing — overlapping reads and writes", () => {
	it("discards a read taken mid-write via the end-of-write marker, then prunes", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })

		store.beginWrite(KEY) // notes a local change at the start
		const midRead = store.beginRead() // a read issued while the write is in flight
		store.patch(KEY, { status: 9 }) // the optimistic edit the write persists
		store.endWrite(KEY) // notes again — supersedes anything issued during the write

		respond(store, "O1", 0, midRead) // the mid-write read returns pre-edit state
		expect(store.peek<Obj>(KEY)!.status).toBe(9) // dropped

		store.endRead(midRead)
		expect(store.pendingChangeCount).toBe(0)
	})

	it("newer overlapping read wins; the older one is dropped, and neither reverts the edit", () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })
		const oldRead = store.beginRead() // revision 0
		store.patch(KEY, { status: 5 }) // local edit, revision 1
		const newRead = store.beginRead() // revision 1

		respond(store, "O1", 7, newRead) // issued at/after the edit → applied
		expect(store.peek<Obj>(KEY)!.status).toBe(7)

		store.endRead(newRead)
		expect(store.pendingChangeCount).toBe(1) // oldRead still predates the edit

		respond(store, "O1", 0, oldRead) // issued before the edit → dropped
		expect(store.peek<Obj>(KEY)!.status).toBe(7) // survives

		store.endRead(oldRead)
		expect(store.pendingChangeCount).toBe(0)
	})

	it("a sparse merge under an in-flight read is unaffected by the prune", () => {
		const store = new EntityStore()
		store.absorb(makeEntity("Objective", "O1", { title: "T", status: 0, college: 3 }),
			["@type", "id", "title", "status", "college"])
		const read = store.beginRead()
		store.patch(KEY, { status: 5 })

		// A sparse response (status only) issued at/after the edit merges without erasing college.
		store.absorb(makeEntity("Objective", "O1", { title: "T", status: 5 }), ["@type", "id", "title", "status"],
			{ issuedAt: store.currentRevision })
		expect(store.peek<Obj>(KEY)!.college).toBe(3)

		store.endRead(read)
		expect(store.pendingChangeCount).toBe(0)
	})
})

describe("editing lifecycle (e2e)", () => {
	it("get, edit + commit, a concurrent stale list dropped, then the ledger empties", async () => {
		const store = new EntityStore()
		const invalidation = { invalidate: () => {} } as unknown as InvalidationScheduler

		// 1) Initial GET resolves the entity, then settles — nothing to prune yet.
		const get = store.beginRead()
		store.absorb(makeEntity("Objective", "O1", { status: 0 }), FIELDS, { issuedAt: get })
		store.endRead(get)
		expect(store.peek<Obj>(KEY)!.status).toBe(0)
		expect(store.pendingChangeCount).toBe(0)

		// 2) A list read for the user's screen goes out and stays in flight, carrying pre-edit state.
		const listRead = store.beginRead()

		// 3) The user edits status → 3 and commits; the write echoes the saved entity back (a read that absorbs).
		await runWrite(store, invalidation, "Objective", "O1", async () => {
			store.patch(KEY, { status: 3 })
			const echo = store.beginRead()
			store.absorb(makeEntity("Objective", "O1", { status: 3 }), FIELDS, { issuedAt: echo })
			store.endRead(echo)
			return { ok: true }
		})
		expect(store.peek<Obj>(KEY)!.status).toBe(3)

		// 4) The list response finally arrives with the stale status 0 — dropped, the edit survives.
		store.absorb(makeEntity("Objective", "O1", { status: 0 }), FIELDS, { issuedAt: listRead })
		expect(store.peek<Obj>(KEY)!.status).toBe(3)

		// 5) The list read settles; nothing else is in flight, so the ledger is fully pruned.
		store.endRead(listRead)
		expect(store.pendingChangeCount).toBe(0)
		expect(store.inFlightReadCount).toBe(0)
	})

	it("a read that throws still deregisters and prunes (the finally in the client's read path)", async () => {
		const store = new EntityStore()
		seed(store, "O1", { status: 0 })

		async function failingRead(): Promise<void> {
			const issuedAt = store.beginRead()
			try {
				await Promise.reject(new Error("network"))
			}
			finally {
				store.endRead(issuedAt)
			}
		}

		await expect(failingRead()).rejects.toThrow("network")
		expect(store.inFlightReadCount).toBe(0)
		expect(store.pendingChangeCount).toBe(0)
	})
})
