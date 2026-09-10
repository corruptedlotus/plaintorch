import { describe, it, expect } from "vitest"
import { InvalidationScheduler, type InvalidationTarget } from "./invalidation"
import { EntityStore } from "./entityStore"
import { makeEntity, recordingInvalidationTarget } from "./test-utils"

describe("InvalidationScheduler — dependents", () => {
	it("dirties an entity and everything the dependency table derives from its canonical instance", async () => {
		const store = new EntityStore()
		store.absorb(
			makeEntity("Objective", "O1", { directiveId: "D1", onrushSprintId: "N1", executives: [{ polarisCycleId: "P1" }] }),
			["@type", "id", "title", "directiveId", "onrushSprintId", "executives"]
		)
		const recording = recordingInvalidationTarget()
		const scheduler = new InvalidationScheduler(store, () => recording.target)

		scheduler.invalidate("Objective", "O1")
		await scheduler.settled()

		// settled() having resolved with these present is itself the regression check: it must wait for the
		// flush, not resolve before it has begun.
		expect(recording.entities).toContain("Objective:O1")
		expect(recording.entities).toContain("StellarDirective:D1")
		expect(recording.entities).toContain("LunarDirective:D1")
		expect(recording.entities).toContain("OnrushSprint:N1")
		expect(recording.entities).toContain("PolarisCycle:P1")
		expect(recording.recordPasses).toBe(1)
	})

	it("coalesces a burst so one write touching several aggregates is a single pass", async () => {
		const store = new EntityStore()
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
		const store = new EntityStore()
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
