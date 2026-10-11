import { describe, it, expect } from "vitest"
import { InvalidationScheduler, type InvalidationTarget } from "./invalidation.js"
import { EntityStore } from "./entityStore.js"
import { entityKey } from "./identity.js"
import { makeEntity, recordingInvalidationTarget, testPolicy } from "./testing.js"

describe("InvalidationScheduler — dependents", () => {
	it("dirties an entity and everything the dependency table derives from its canonical instance", async () => {
		const store = new EntityStore(testPolicy)
		store.absorb(
			makeEntity("Task", "T1", { projectId: "P1", assignees: [{ userId: "U1" }, { userId: "U2" }] }),
			["@type", "id", "title", "projectId", "assignees"]
		)
		const recording = recordingInvalidationTarget()
		const scheduler = new InvalidationScheduler(store, () => recording.target, {
			Task: (task) => [
				entityKey("Project", task.projectId as string),
				...(task.assignees as { userId: string }[]).map((assignee) => entityKey("User", assignee.userId)),
				undefined
			]
		})

		// The call names only the task; its relations are read off the instance the store holds.
		scheduler.invalidate("Task", "T1")
		await scheduler.settled()

		// settled() having resolved with these present is itself the regression check: it must wait for the
		// flush, not resolve before it has begun.
		expect(new Set(recording.entities)).toEqual(new Set(["Task:T1", "Project:P1", "User:U1", "User:U2"]))
		expect(recording.recordPasses).toBe(1)
	})

	it("a type the table does not name, or no table at all, dirties only the entity and the records", async () => {
		const store = new EntityStore(testPolicy)
		store.absorb(makeEntity("Task", "T1", { projectId: "P1" }))
		const recording = recordingInvalidationTarget()
		const scheduler = new InvalidationScheduler(store, () => recording.target)

		scheduler.invalidate("Task", "T1")
		await scheduler.settled()

		expect(recording.entities).toEqual(["Task:T1"])
		expect(recording.recordPasses).toBe(1)
	})

	it("an entity the store does not hold has no relations to follow", async () => {
		const store = new EntityStore(testPolicy)
		const recording = recordingInvalidationTarget()
		const scheduler = new InvalidationScheduler(store, () => recording.target, {
			Task: () => [entityKey("Project", "P1")]
		})

		scheduler.invalidate("Task", "T1")
		await scheduler.settled()

		expect(recording.entities).toEqual(["Task:T1"])
	})

	it("coalesces a burst so one write touching several aggregates is a single pass", async () => {
		const store = new EntityStore(testPolicy)
		const recording = recordingInvalidationTarget()
		const scheduler = new InvalidationScheduler(store, () => recording.target)

		scheduler.invalidate("Objective", "O1")
		scheduler.invalidate("Objective", "O2")
		scheduler.invalidate("Fate", "F1")
		await scheduler.settled()

		expect(recording.recordPasses).toBe(1)
		expect(new Set(recording.entities)).toEqual(new Set(["Objective:O1", "Objective:O2", "Fate:F1"]))
	})

	it("drains work queued mid-flush on the same chain — never a second, concurrent flush", async () => {
		const store = new EntityStore(testPolicy)
		const entities: string[] = []
		let passes = 0
		let injected = false
		let scheduler!: InvalidationScheduler
		const target: InvalidationTarget = {
			async revalidateEntityIfObserved(key) {
				entities.push(key)
				if (!injected) {
					injected = true
					scheduler.invalidate("Objective", "B") // queued while the first pass is running
				}
			},
			async revalidateRecordsIfObserved() {
				passes++
			}
		}
		scheduler = new InvalidationScheduler(store, () => target)

		scheduler.invalidate("Objective", "A")
		await scheduler.settled()

		expect(entities).toContain("Objective:A")
		expect(entities).toContain("Objective:B")
		expect(passes).toBe(2) // two sequential passes, not one flush racing another
	})
})
