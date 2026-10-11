import { describe, it, expect } from "vitest"
import { EntityStore } from "./entityStore"
import { DerivedRepository } from "./derivedRepository"
import { detachFrom } from "./detach"
import { identifyByShape } from "./identity"
import { resolvePolicy, type EntityPolicy } from "./policy"
import { makeEntity, flushMicrotasks, testPolicy } from "./test-utils"

// What still holds an entity once it is removed. Under "detach" — the default — the store takes it out of every
// collection it can reach, so a listing loses the row and an owner loses the member at the moment of removal
// rather than at the next refetch. Under "keep" nothing else is touched. The mode is the store's policy, for
// every type or per type.

type Row = Record<string, unknown>

const identify = testPolicy.identify
const entity = (typeName: string, id: string, extra: Row = {}) => makeEntity(typeName, id, extra) as Row

describe("detachFrom", () => {
	it("returns a new array without the instance, leaving the original untouched", () => {
		const target = entity("Task", "T1")
		const other = entity("Task", "T2")
		const members = [other, target]

		const detached = detachFrom(members, target, identify)

		expect(detached.changed).toBe(true)
		expect(detached.value).toEqual([other])
		expect(detached.value).not.toBe(members)
		expect(members).toHaveLength(2)
	})

	it("returns the very same value, unchanged, when nothing holds the instance", () => {
		const members = [entity("Task", "T2")]
		const view = { members, total: 1 }

		expect(detachFrom(members, entity("Task", "T1"), identify)).toEqual({ value: members, changed: false })
		const detached = detachFrom(view, entity("Task", "T1"), identify)
		expect(detached.value).toBe(view)
		expect(view.members).toBe(members)
	})

	it("takes out every occurrence", () => {
		const target = entity("Task", "T1")
		expect(detachFrom([target, entity("Task", "T2"), target], target, identify).value).toHaveLength(1)
	})

	it("replaces a collection on the object that owns it, at any depth short of another entity", () => {
		const target = entity("Task", "T1")
		const kept = entity("Task", "T2")
		const owner = entity("Project", "P1", {
			tasks: [target, kept],
			board: { columns: [{ name: "doing", tasks: [target] }, { name: "done", tasks: [kept] }] }
		})
		const tasksBefore = owner.tasks
		const doneBefore = (owner.board as { columns: { tasks: unknown[] }[] }).columns[1]!.tasks

		const detached = detachFrom(owner, target, identify)

		expect(detached.value).toBe(owner)
		expect(detached.changed).toBe(true)
		expect(owner.tasks).toEqual([kept])
		expect(owner.tasks).not.toBe(tasksBefore)
		const columns = (owner.board as { columns: { tasks: unknown[] }[] }).columns
		expect(columns[0]!.tasks).toEqual([])
		// A collection that did not hold it keeps its reference.
		expect(columns[1]!.tasks).toBe(doneBefore)
	})

	it("stops at another entity — that entity is a root of its own", () => {
		const target = entity("Task", "T1")
		const nested = entity("Project", "P2", { tasks: [target] })
		const owner = entity("Portfolio", "F1", { projects: [nested] })

		expect(detachFrom(owner, target, identify).changed).toBe(false)
		expect(nested.tasks).toEqual([target])
	})

	it("leaves a single reference pointing at the instance", () => {
		const target = entity("User", "U1")
		const owner = entity("Task", "T1", { assignee: target, watchers: [target] })

		detachFrom(owner, target, identify)

		expect(owner.assignee).toBe(target)
		expect(owner.watchers).toEqual([])
	})

	it("terminates on a cycle that does not run through an entity", () => {
		const target = entity("Task", "T1")
		const a: Row = { items: [target] }
		const b: Row = { back: a }
		a.forward = b

		expect(detachFrom(a, target, identify).changed).toBe(true)
		expect(a.items).toEqual([])
	})

	it("does not write to a frozen holder", () => {
		const target = entity("Task", "T1")
		const frozen = Object.freeze({ items: [target] })

		expect(() => detachFrom(frozen, target, identify)).not.toThrow()
		expect(frozen.items).toEqual([target])
	})
})

describe("EntityStore.remove — detaching from the entities that hold it", () => {
	function seeded(policy: EntityPolicy = testPolicy) {
		const store = new EntityStore(policy)
		const task = store.absorb(entity("Task", "T1"))
		const other = store.absorb(entity("Task", "T2"))
		const project = store.absorb(entity("Project", "P1", { tasks: [task, other] }))
		const bystander = store.absorb(entity("Project", "P2", { tasks: [other] }))
		return { store, task, other, project, bystander }
	}

	it("takes the entity out of an owner's collection, as a new array, and tells the owner's observers", () => {
		const { store, task, other, project } = seeded()
		const tasksBefore = project.tasks
		let notified = 0
		store.subscribe("Project:P1", () => notified++)
		const versionBefore = store.version("Project:P1")

		store.remove("Task:T1")

		expect(project.tasks).toEqual([other])
		expect(project.tasks).not.toBe(tasksBefore)
		expect(tasksBefore).toContain(task)
		expect(notified).toBe(1)
		expect(store.version("Project:P1")).toBe(versionBefore + 1)
		// The owner lost a member the backend removed; what else changed about it has not been read yet.
		expect(store.isStale("Project:P1")).toBe(true)
	})

	it("leaves an entity that did not hold it alone — no notification, no new array, not stale", () => {
		const { store, bystander } = seeded()
		const tasksBefore = bystander.tasks
		let notified = 0
		store.subscribe("Project:P2", () => notified++)

		store.remove("Task:T1")

		expect(bystander.tasks).toBe(tasksBefore)
		expect(notified).toBe(0)
		expect(store.isStale("Project:P2")).toBe(false)
	})

	it("an observer of the removed entity already finds it gone from its owners when told", () => {
		const { store, project } = seeded()
		let membersWhenTold = -1
		store.subscribe("Task:T1", () => { membersWhenTold = (project.tasks as unknown[]).length })

		store.remove("Task:T1")

		expect(membersWhenTold).toBe(1)
	})

	it("tells the owners' type observers along with the removed entity's", async () => {
		const { store } = seeded()
		await flushMicrotasks()
		const told: string[] = []
		store.subscribeType("Project", () => told.push("Project"))
		store.subscribeType("Task", () => told.push("Task"))

		store.remove("Task:T1")
		await flushMicrotasks()

		expect(told.sort()).toEqual(["Project", "Task"])
	})

	it("under \"keep\" touches nothing but the identity itself", () => {
		const { store, task, project } = seeded({ ...testPolicy, removal: "keep" })
		const tasksBefore = project.tasks
		let notified = 0
		store.subscribe("Project:P1", () => notified++)

		store.remove("Task:T1")

		expect(project.tasks).toBe(tasksBefore)
		expect(project.tasks).toContain(task)
		expect(notified).toBe(0)
		expect(store.stateOf("Task:T1")).toBe("not-loaded")
	})

	it("takes the mode per type when the policy gives one per type", () => {
		const store = new EntityStore({
			identify: identifyByShape({ typeKey: "@type" }),
			removal: (typeName) => typeName === "Draft" ? "keep" : "detach"
		})
		const task = store.absorb(entity("Task", "T1"))
		const draft = store.absorb(entity("Draft", "D1"))
		const project = store.absorb(entity("Project", "P1", { tasks: [task], drafts: [draft] }))

		store.remove("Task:T1")
		store.remove("Draft:D1")

		expect(project.tasks).toEqual([])
		expect(project.drafts).toEqual([draft])
	})

	it("defaults to detach, for every type", () => {
		const resolved = resolvePolicy({ identify })
		expect(resolved.removal("Anything")).toBe("detach")
		expect(resolvePolicy({ identify, removal: "keep" }).removal("Anything")).toBe("keep")
	})
})

describe("DerivedRepository — letting go of a removed entity", () => {
	async function listing(policy: EntityPolicy = testPolicy) {
		const store = new EntityStore(policy)
		const task = store.absorb(entity("Task", "T1"))
		const other = store.absorb(entity("Task", "T2"))
		let calls = 0
		const repository = new DerivedRepository<unknown[]>(store, async () => {
			calls++
			return [task, other]
		})
		let notified = 0
		repository.subscribe("", () => notified++)
		await repository.get()
		return { store, task, other, repository, notifications: () => notified, calls: () => calls }
	}

	it("a listing loses the row at once, as a new array, and tells its observers — without a refetch", async () => {
		const h = await listing()
		const before = h.repository.peek()

		h.store.remove("Task:T1")

		expect(h.repository.peek()).toEqual([h.other])
		expect(h.repository.peek()).not.toBe(before)
		expect(h.notifications()).toBe(2)
		expect(h.calls()).toBe(1)
	})

	it("a record that did not hold it is not touched and tells nobody", async () => {
		const h = await listing()
		const before = h.repository.peek()
		h.store.absorb(entity("Task", "T9"))

		h.store.remove("Task:T9")

		expect(h.repository.peek()).toBe(before)
		expect(h.notifications()).toBe(1)
	})

	it("a view with the entity nested inside it loses it there", async () => {
		const store = new EntityStore(testPolicy)
		const task = store.absorb(entity("Task", "T1"))
		const repository = new DerivedRepository<{ today: unknown[], total: number }>(store, async () => ({ today: [task], total: 1 }))
		const view = (await repository.get())!

		store.remove("Task:T1")

		expect(repository.peek()).toBe(view)
		expect(view.today).toEqual([])
	})

	it("a record that was the entity itself resolves to nothing and is left stale", async () => {
		const store = new EntityStore(testPolicy)
		const sprint = store.absorb(entity("Sprint", "S1"))
		let calls = 0
		const current = new DerivedRepository<unknown>(store, async () => { calls++; return sprint })
		let notified = 0
		current.subscribe("", () => notified++)
		await current.get()

		store.remove("Sprint:S1")

		expect(current.peek()).toBeUndefined()
		expect(current.isResolved()).toBe(true)
		expect(notified).toBe(2)
		// Which entity it now names is the backend's to say, so the next read asks.
		await current.get()
		expect(calls).toBe(2)
	})

	it("every key of a keyed repository lets go", async () => {
		const store = new EntityStore(testPolicy)
		const task = store.absorb(entity("Task", "T1"))
		const repository = new DerivedRepository<unknown[]>(store, async (key) => key === "empty" ? [] : [task])
		await repository.get("a")
		await repository.get("b")
		await repository.get("empty")

		store.remove("Task:T1")

		expect(repository.peek("a")).toEqual([])
		expect(repository.peek("b")).toEqual([])
	})

	it("under \"keep\" a listing goes on holding the instance until it is refetched", async () => {
		const h = await listing({ ...testPolicy, removal: "keep" })
		const before = h.repository.peek()

		h.store.remove("Task:T1")

		expect(h.repository.peek()).toBe(before)
		expect(h.repository.peek()).toContain(h.task)
		expect(h.notifications()).toBe(1)
	})
})
