import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import { EventiveResolution, OrbitUnit } from "./declaratives/models"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "./internal/transport"

/** One request as the core would receive it: the body JSON round-tripped, so undefined keys drop and null stays. */
interface WireRequest {
	method: PlaintorchCoreRequest["method"]
	path: string
	body: unknown
}

/** A transport that records every request as it crosses the wire and answers 200 with a fixed body. */
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

describe("manual eventives", () => {
	it("createEventive posts the plan's epoch, omitting an absent duration and zone", async () => {
		const { client, requests } = recordingClient()

		await client.declaratives.createEventive({
			fateId: "F1",
			epoch: { moment: "2026-10-02T18:30:00", granularity: OrbitUnit.Minute, duration: "1h30m", timeZone: undefined }
		})

		expect(requests[0]?.method).toBe("POST")
		expect(requests[0]?.path).toBe("/api/eventives")
		expect(requests[0]?.body).toEqual({
			fateId: "F1",
			epoch: { moment: "2026-10-02T18:30:00", granularity: OrbitUnit.Minute, duration: "1h30m" }
		})
	})

	it("updateEventive addresses a manual eventive by its row id alone", async () => {
		const { client, requests } = recordingClient()

		await client.declaratives.updateEventive({ id: 41 }, { resolution: EventiveResolution.Cancelled })

		expect(requests[0]?.method).toBe("PUT")
		expect(requests[0]?.path).toBe("/api/eventives")
		expect(requests[0]?.body).toEqual({ occurrence: { id: 41 }, update: { resolution: EventiveResolution.Cancelled } })
	})

	it("a manual eventive revives with no recurrence-id and reads as manual", async () => {
		const { client } = recordingClient(JSON.stringify({
			"@type": "Eventive",
			id: 41,
			fateId: "F1",
			resolution: 0,
			recurrenceId: null,
			recurrenceOwnerUid: "F1",
			isManual: true,
			epoch: { moment: "2026-10-02T18:30:00", granularity: 6, date: "2026-10-02", isAllDay: false, timeOfDay: "18:30:00", endMoment: "2026-10-02T18:31:00" }
		}))

		const eventive = await client.declaratives.createEventive({ fateId: "F1", epoch: { moment: "2026-10-02T18:30:00", granularity: OrbitUnit.Minute } })

		expect(eventive?.id).toBe(41)
		expect(eventive?.recurrenceId ?? undefined).toBeUndefined()
		expect(eventive?.isManual).toBe(true)
	})
})
