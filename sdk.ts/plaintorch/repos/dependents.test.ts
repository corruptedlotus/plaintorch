import { describe, it, expect } from "vitest"
import { plaintorchDependents } from "./dependents"
import { plaintorchEntityPolicy } from "./policy"
import { EntityStore, InvalidationScheduler } from "@corruptedlotus/local-repo"
import { makeEntity, recordingInvalidationTarget } from "@corruptedlotus/local-repo/testing"

// What a change to each PLAINTORCH entity makes stale. The scheduler's own mechanics are covered beside it;
// this pins the table PLAINTORCH hands it.

function resolve(typeName: string, fields: Record<string, unknown>): (string | undefined)[] {
	return plaintorchDependents[typeName]!(makeEntity(typeName, "X", fields))
}

describe("plaintorchDependents", () => {
	it("dirties an objective and everything the table derives from its canonical instance", async () => {
		const store = new EntityStore(plaintorchEntityPolicy)
		store.absorb(
			makeEntity("Objective", "O1", { directiveId: "D1", onrushSprintId: "N1", executives: [{ polarisCycleId: "P1" }] }),
			["@type", "id", "title", "directiveId", "onrushSprintId", "executives"]
		)
		const recording = recordingInvalidationTarget()
		const scheduler = new InvalidationScheduler(store, () => recording.target, plaintorchDependents)

		scheduler.invalidate("Objective", "O1")
		await scheduler.settled()

		expect(recording.entities).toContain("Objective:O1")
		expect(recording.entities).toContain("StellarDirective:D1")
		expect(recording.entities).toContain("LunarDirective:D1")
		expect(recording.entities).toContain("OnrushSprint:N1")
		expect(recording.entities).toContain("PolarisCycle:P1")
		expect(recording.recordPasses).toBe(1)
	})

	it("names both directive kinds for a directive reference, and both incentive kinds for a parent", () => {
		expect(resolve("Objective", { directiveId: "D1", parentIncentiveId: "I1" })).toEqual(
			expect.arrayContaining(["StellarDirective:D1", "LunarDirective:D1", "Objective:I1", "Fate:I1"])
		)
		expect(resolve("Fate", { directiveId: "D1", parentIncentiveId: "F0" })).toEqual(
			["StellarDirective:D1", "LunarDirective:D1", "Fate:F0"]
		)
		expect(resolve("StellarDirective", { parentDirectiveId: "D0" })).toEqual(["StellarDirective:D0", "LunarDirective:D0"])
		expect(resolve("LunarDirective", { parentDirectiveId: "D0" })).toEqual(["StellarDirective:D0", "LunarDirective:D0"])
	})

	it("follows a decree to its directive and every cycle holding it", () => {
		expect(resolve("Decree", { directiveId: "D1", executives: [{ polarisCycleId: "P1" }, { polarisCycleId: "P2" }] })).toEqual(
			["StellarDirective:D1", "LunarDirective:D1", "PolarisCycle:P1", "PolarisCycle:P2"]
		)
	})

	it("links a sprint with its executive orders in both directions, and a lore page with its parent", () => {
		expect(resolve("OnrushSprint", { executiveOrders: [{ id: "E1" }, { id: "E2" }] })).toEqual(["ExecutiveOrder:E1", "ExecutiveOrder:E2"])
		expect(resolve("ExecutiveOrder", { onrushSprintId: "N1" })).toEqual(["OnrushSprint:N1"])
		expect(resolve("LorePage", { parentId: "L0" })).toEqual(["LorePage:L0"])
	})

	it("leaves an absent relation undefined rather than naming an empty identity", () => {
		expect(resolve("Objective", {}).filter((key) => key !== undefined)).toEqual([])
		expect(resolve("LorePage", { parentId: "" })).toEqual([undefined])
		expect(resolve("OnrushSprint", {})).toEqual([])
	})
})
