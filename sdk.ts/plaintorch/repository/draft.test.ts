import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { EntityRepository } from "./entityRepository"
import type { InvalidationScheduler } from "./invalidation"
import { makeEntity, testPolicy } from "./test-utils"

// The fork-and-commit editing mode. A draft is an isolated copy of an entity; nothing reaches the store until
// commit, which diffs the copy against the fork-time baseline and sends only what changed.

type Thing = { "@type": string, id: string, title: string, status: number, tags: string[], note?: string }

function harness() {
	const store = new EntityStore(testPolicy)
	const invalidated: string[] = []
	const invalidation = { invalidate: (typeName: string, id: string) => { invalidated.push(`${typeName}:${id}`) } } as unknown as InvalidationScheduler
	const repository = new EntityRepository<Thing>(store, "Thing", async () => undefined, { invalidation })
	const thing = store.absorb(makeEntity("Thing", "1", { status: 0, tags: ["a"] })) as unknown as Thing
	return { store, repository, thing, invalidated }
}

describe("EntityDraft — forking", () => {
	it("is an isolated copy: edits do not reach the canonical instance before commit", () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!

		draft.value.title = "renamed"
		draft.value.status = 3

		expect(thing.title).toBe("1")
		expect(thing.status).toBe(0)
		expect(draft.value).not.toBe(thing)
	})

	it("reports only the fields that differ from the fork-time baseline", () => {
		const { repository } = harness()
		const draft = repository.fork("1")!
		expect(draft.dirty).toBe(false)
		expect(draft.diff()).toEqual({})

		draft.value.status = 3
		draft.value.note = "added"
		draft.value.title = "1" // set to the value it already has

		expect(draft.dirty).toBe(true)
		expect(draft.diff()).toEqual({ status: 3, note: "added" })
	})

	it("is undefined for an entity the store does not hold", () => {
		const { repository } = harness()
		expect(repository.fork("nope")).toBeUndefined()
	})

	it("shares nested collections with the canonical instance — the documented shallow caveat", () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!

		draft.value.tags.push("b")

		// The push went straight through to the canonical instance, and the diff cannot see it either.
		expect(thing.tags).toEqual(["a", "b"])
		expect(draft.diff()).toEqual({})
	})
})

describe("EntityDraft — committing", () => {
	it("applies the patch to the canonical instance, sends only the patch, and invalidates", async () => {
		const { repository, thing, invalidated } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3
		const sent: unknown[] = []

		const ok = await draft.commit(async (patch, entity) => {
			sent.push({ patch, entity })
			return true
		})

		expect(ok).toBe(true)
		expect(thing.status).toBe(3)
		expect(sent).toEqual([{ patch: { status: 3 }, entity: thing }])
		expect(invalidated).toEqual(["Thing:1"])
	})

	it("the canonical instance already carries the patch when persist runs", async () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3
		let statusWhenSent = -1

		await draft.commit(async () => { statusWhenSent = thing.status; return true })

		expect(statusWhenSent).toBe(3)
	})

	it("a concurrent edit to a field the draft did not touch survives the commit", async () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3
		// Meanwhile another surface renamed the entity.
		thing.title = "renamed elsewhere"

		await draft.commit(async () => true)

		expect(thing.status).toBe(3)
		expect(thing.title).toBe("renamed elsewhere")
	})

	it("a rejected commit restores the canonical instance and invalidates nothing", async () => {
		const { repository, thing, invalidated } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3

		expect(await draft.commit(async () => undefined)).toBe(false)

		expect(thing.status).toBe(0)
		expect(invalidated).toEqual([])
	})

	it("a persist that throws restores the canonical instance and rethrows", async () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3

		await expect(draft.commit(async () => { throw new Error("boom") })).rejects.toThrow("boom")

		expect(thing.status).toBe(0)
		expect(repository.isMutating("1")).toBe(false)
	})

	it("holds the identity while the write is in flight", async () => {
		const { repository } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3
		let heldDuring = false

		await draft.commit(async () => { heldDuring = repository.isMutating("1"); return true })

		expect(heldDuring).toBe(true)
		expect(repository.isMutating("1")).toBe(false)
	})

	it("a commit with nothing changed is a no-op that succeeds without sending", async () => {
		const { repository, invalidated } = harness()
		const draft = repository.fork("1")!
		let sent = 0

		expect(await draft.commit(async () => { sent++; return true })).toBe(true)

		expect(sent).toBe(0)
		expect(invalidated).toEqual([])
	})

	it("is spent after one commit — a second commit sends nothing and reports false", async () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3
		let sent = 0
		await draft.commit(async () => { sent++; return true })

		draft.value.status = 9
		expect(await draft.commit(async () => { sent++; return true })).toBe(false)

		expect(sent).toBe(1)
		expect(thing.status).toBe(3)
	})

	it("cancel throws the copy away: nothing was applied, and a later commit sends nothing", async () => {
		const { repository, thing } = harness()
		const draft = repository.fork("1")!
		draft.value.status = 3
		let sent = 0

		draft.cancel()
		expect(await draft.commit(async () => { sent++; return true })).toBe(false)

		expect(thing.status).toBe(0)
		expect(sent).toBe(0)
	})

	it("tells the entity's observers once the patch is applied, and again on rollback", async () => {
		const { repository } = harness()
		let notified = 0
		repository.subscribe("1", () => notified++)
		const draft = repository.fork("1")!
		draft.value.status = 3

		await draft.commit(async () => undefined)

		expect(notified).toBe(2)
	})
})
