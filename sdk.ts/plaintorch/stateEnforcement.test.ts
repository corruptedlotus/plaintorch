import { describe, expect, it, vi } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import {
	BacklogStateEnforcementLevel,
	forceCompensationValue,
	StateDecisionOutcome,
	stateEnforcementHeader,
	type StateEnforcementProblem
} from "./enforcement/models"
import { LifecycleState } from "./schedules/models"
import { enteredStates, type LifecycleKind } from "./lifecycle/models"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "./internal/transport"

function problem(outcome: StateDecisionOutcome): StateEnforcementProblem {
	return {
		type: "state-enforcement",
		outcome,
		level: BacklogStateEnforcementLevel.Warn,
		message: "Objective 'Tower' cannot start: it is blocked by 1 dependency. Going ahead would also finish 1 item.",
		conflicts: [],
		compensations: [{
			subject: { kind: "StellarDirective", id: "D1", title: "Foundation" },
			from: LifecycleState.Open,
			to: LifecycleState.Finished,
			status: "Fulfilled",
			reason: "A dependency needs it."
		}]
	}
}

/** A transport whose every answer `respond` decides, recording the requests it was sent. */
function fakeTransport(respond: (request: PlaintorchCoreRequest) => { status: number, body: unknown }) {
	const requests: PlaintorchCoreRequest[] = []
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse> {
			requests.push(request)
			const { status, body } = respond(request)
			const text = JSON.stringify(body)
			return {
				ok: status >= 200 && status < 300,
				status,
				async text() {
					return text
				},
				header() {
					return undefined
				}
			}
		}
	}
	return { transport, requests }
}

const forced = (request: PlaintorchCoreRequest) => request.headers?.[stateEnforcementHeader] === forceCompensationValue

describe("core client state enforcement (PEP103)", () => {
	it("asks the handler and repeats a held-back write forcing its compensations", async () => {
		const { transport, requests } = fakeTransport(request => forced(request)
			? { status: 200, body: { id: "O1" } }
			: { status: 409, body: problem(StateDecisionOutcome.Confirm) })
		const client = new PlaintorchCoreClient({ transports: [transport] })
		const confirm = vi.fn(async (_problem: StateEnforcementProblem) => true)
		client.stateEnforcement = { confirm }

		const result = await client.putForJson<{ id: string }>("/api/objectives/O1/workflow", { status: 3 })

		expect(result?.id).toBe("O1")
		expect(confirm).toHaveBeenCalledOnce()
		expect(confirm.mock.calls[0]?.[0]?.compensations[0]?.subject.title).toBe("Foundation")
		expect(requests).toHaveLength(2)
		expect(forced(requests[0]!)).toBe(false)
		expect(forced(requests[1]!)).toBe(true)
		expect(requests[1]!.body).toEqual({ status: 3 })
	})

	it("cancels a held-back write the handler declines", async () => {
		const { transport, requests } = fakeTransport(() => ({ status: 409, body: problem(StateDecisionOutcome.Confirm) }))
		const client = new PlaintorchCoreClient({ transports: [transport] })
		client.stateEnforcement = { confirm: async () => false }

		expect(await client.putJson("/api/objectives/O1/workflow", { status: 3 })).toBe(false)
		expect(requests).toHaveLength(1)
	})

	it("cancels a held-back write when nothing answers for the client", async () => {
		const { transport, requests } = fakeTransport(() => ({ status: 409, body: problem(StateDecisionOutcome.Confirm) }))
		const client = new PlaintorchCoreClient({ transports: [transport] })

		expect(await client.putForJson("/api/objectives/O1/workflow", { status: 3 })).toBeUndefined()
		expect(requests).toHaveLength(1)
	})

	it("tells the handler of a refusal and never repeats it", async () => {
		const { transport, requests } = fakeTransport(() => ({ status: 409, body: problem(StateDecisionOutcome.Refuse) }))
		const client = new PlaintorchCoreClient({ transports: [transport] })
		const confirm = vi.fn(async (_problem: StateEnforcementProblem) => true)
		const refused = vi.fn((_problem: StateEnforcementProblem) => { })
		client.stateEnforcement = { confirm, refused }

		expect(await client.putForJson("/api/objectives/O1/workflow", { status: 3 })).toBeUndefined()
		expect(confirm).not.toHaveBeenCalled()
		expect(refused).toHaveBeenCalledOnce()
		expect(refused.mock.calls[0]?.[0]?.message).toContain("blocked by 1 dependency")
		expect(requests).toHaveLength(1)
	})

	it("never asks again about a write it already forced", async () => {
		const { transport, requests } = fakeTransport(() => ({ status: 409, body: problem(StateDecisionOutcome.Confirm) }))
		const client = new PlaintorchCoreClient({ transports: [transport] })
		const confirm = vi.fn(async (_problem: StateEnforcementProblem) => true)
		client.stateEnforcement = { confirm }

		expect(await client.putForJson("/api/objectives/O1/workflow", { status: 3 })).toBeUndefined()
		expect(confirm).toHaveBeenCalledOnce()
		expect(requests).toHaveLength(2)
	})

	it("leaves a 409 of another kind to the next transport", async () => {
		const other = fakeTransport(() => ({ status: 409, body: { title: "Conflict" } }))
		const fallback = fakeTransport(() => ({ status: 200, body: { id: "O1" } }))
		const client = new PlaintorchCoreClient({ transports: [other.transport, fallback.transport] })

		expect((await client.putForJson<{ id: string }>("/api/objectives/O1", {}))?.id).toBe("O1")
		expect(fallback.requests).toHaveLength(1)
	})

	it("hands a write sent in an answering scope back to that scope, not to the client-wide handler", async () => {
		const { transport, requests } = fakeTransport(() => ({ status: 409, body: problem(StateDecisionOutcome.Confirm) }))
		const client = new PlaintorchCoreClient({ transports: [transport] })
		const confirm = vi.fn(async (_problem: StateEnforcementProblem) => true)
		client.stateEnforcement = { confirm }
		let answered: StateEnforcementProblem | undefined

		const result = await client.withStateEnforcement({ answer: problem => answered = problem },
			() => client.putForJson("/api/objectives/O1/workflow", { status: 3 }))

		expect(result).toBeUndefined()
		expect(answered?.outcome).toBe(StateDecisionOutcome.Confirm)
		expect(confirm).not.toHaveBeenCalled()
		expect(requests).toHaveLength(1)
	})

	it("forces the writes a forcing scope sends", async () => {
		const { transport, requests } = fakeTransport(request => forced(request)
			? { status: 200, body: { id: "O1" } }
			: { status: 409, body: problem(StateDecisionOutcome.Confirm) })
		const client = new PlaintorchCoreClient({ transports: [transport] })

		const result = await client.withStateEnforcement({ force: true },
			() => client.putForJson<{ id: string }>("/api/objectives/O1/workflow", { status: 3 }))

		expect(result?.id).toBe("O1")
		expect(requests).toHaveLength(1)
		expect(forced(requests[0]!)).toBe(true)
	})

	it("covers only what is sent before the first await", async () => {
		const { transport, requests } = fakeTransport(() => ({ status: 200, body: { id: "O1" } }))
		const client = new PlaintorchCoreClient({ transports: [transport] })

		await client.withStateEnforcement({ force: true }, async () => {
			await client.putForJson("/api/objectives/O1/workflow", { status: 3 })
			await client.putForJson("/api/objectives/O2/workflow", { status: 3 })
		})

		expect(requests.map(forced)).toEqual([true, false])
	})
})

describe("lifecycle transitions (PEP103)", () => {
	const objective: LifecycleKind = {
		kind: "Objective",
		states: [LifecycleState.Deferred, LifecycleState.Open, LifecycleState.Started, LifecycleState.Finished],
		statuses: []
	}
	const checkpoint: LifecycleKind = {
		kind: "Checkpoint",
		states: [LifecycleState.Deferred, LifecycleState.Open, LifecycleState.Finished],
		statuses: []
	}

	it("enters every progress state a jump passes", () => {
		expect(enteredStates(objective, LifecycleState.Open, LifecycleState.Finished)).toEqual([LifecycleState.Started, LifecycleState.Finished])
		expect(enteredStates(objective, LifecycleState.Started, LifecycleState.Finished)).toEqual([LifecycleState.Finished])
	})

	it("reads a resume as from before Open, and a deferral or a move back as entering nothing", () => {
		expect(enteredStates(objective, LifecycleState.Deferred, LifecycleState.Started)).toEqual([LifecycleState.Started])
		expect(enteredStates(objective, LifecycleState.Started, LifecycleState.Deferred)).toEqual([])
		expect(enteredStates(objective, LifecycleState.Finished, LifecycleState.Open)).toEqual([])
	})

	it("skips the states a kind never passes through", () => {
		expect(enteredStates(checkpoint, LifecycleState.Open, LifecycleState.Finished)).toEqual([LifecycleState.Finished])
	})
})
