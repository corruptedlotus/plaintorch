import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "../coreClient"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "../internal/transport"
import { BacklogSchedule, BacklogScheduleType, LifecyclePhase } from "./models"

interface WireRequest {
	method: PlaintorchCoreRequest["method"]
	path: string
	body: unknown
}

/** A transport that records each request as it crosses the wire and answers 200 with a fixed body. */
function recordingClient(responseBody = "{}") {
	const requests: WireRequest[] = []
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse> {
			requests.push({
				method: request.method,
				path: request.path,
				body: request.body === undefined ? undefined : JSON.parse(JSON.stringify(request.body))
			})
			return {
				ok: true,
				status: 200,
				async text() {
					return responseBody
				},
				header() {
					return undefined
				}
			}
		}
	}

	return { client: new PlaintorchCoreClient({ transports: [transport] }), requests }
}

/** A schedule as the core serializes it: numeric type and state, a civil time, every owner key present. */
const servedSchedule = {
	"@type": "BacklogSchedule",
	id: 12,
	type: 1,
	state: 0,
	time: "2026-10-05T09:30:00",
	incentiveId: "j00000001",
	incentive: null,
	directiveId: null,
	directive: null,
	checkpointId: null,
	checkpoint: null,
	ownerId: "j00000001"
}

describe("backlog schedules (PEP101)", () => {
	it("the enums match the core's numeric wire form", () => {
		expect(BacklogScheduleType.Plan).toBe(0)
		expect(BacklogScheduleType.Record).toBe(1)
		expect(LifecyclePhase.Begin).toBe(0)
		expect(LifecyclePhase.Finish).toBe(1)
	})

	it("list reads every schedule, or one owner's", async () => {
		const { client, requests } = recordingClient(JSON.stringify([servedSchedule]))

		const all = await client.schedules.list()
		await client.schedules.list("A 1")

		expect(requests.map(request => `${request.method} ${request.path}`)).toEqual([
			"GET /api/schedules",
			"GET /api/schedules?entityId=A%201"
		])
		const schedule = all[0]!
		expect(schedule).toBeInstanceOf(BacklogSchedule)
		expect(schedule.type).toBe(BacklogScheduleType.Record)
		expect(schedule.state).toBe(LifecyclePhase.Begin)
		expect(schedule.time).toBe("2026-10-05T09:30:00")
		expect(schedule.ownerId).toBe("j00000001")
	})

	it("add posts the owner, the numeric type and state, and the civil time", async () => {
		const { client, requests } = recordingClient(JSON.stringify(servedSchedule))

		await client.schedules.add({ entityId: "A00000001", type: BacklogScheduleType.Plan, state: LifecyclePhase.Finish, time: "2026-12-01T00:00:00" })

		expect(requests[0]?.method).toBe("POST")
		expect(requests[0]?.path).toBe("/api/schedules")
		expect(requests[0]?.body).toEqual({ entityId: "A00000001", type: 0, state: 1, time: "2026-12-01T00:00:00" })
	})

	it("delete addresses the schedule by id", async () => {
		const { client, requests } = recordingClient("")

		await client.schedules.delete(12)

		expect(requests[0]?.method).toBe("DELETE")
		expect(requests[0]?.path).toBe("/api/schedules/12")
	})
})
