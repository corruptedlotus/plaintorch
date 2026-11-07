import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import { Occurrence, Terminative, TerminativeResolution } from "./declaratives/models"
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

const terminativeJson = {
	"@type": "Terminative",
	id: 12,
	objectiveId: "J1",
	directiveId: null,
	checkpointId: null,
	objective: { "@type": "Objective", id: "J1", title: "Deliver" },
	resolution: 0,
	ignored: true,
	recurrenceId: "2026-10-20T00:00:00",
	recurrenceOwnerUid: "J1",
	isManual: false,
	epoch: { moment: "2026-10-20T00:00:00", granularity: 4, date: "2026-10-20", isAllDay: true, timeOfDay: null, endMoment: "2026-10-21T00:00:00" }
}

describe("terminatives", () => {
	it("updateTerminative addresses a projected terminative by its origin and sends only the flag", async () => {
		const { client, requests } = recordingClient()

		await client.declaratives.updateTerminative({ originId: "J1" }, { ignored: true })

		expect(requests[0]?.method).toBe("PUT")
		expect(requests[0]?.path).toBe("/api/terminatives")
		expect(requests[0]?.body).toEqual({ occurrence: { originId: "J1" }, update: { ignored: true } })
	})

	it("listTerminatives narrows to one origin", async () => {
		const { client, requests } = recordingClient("[]")

		await client.declaratives.listTerminatives("c-gate")

		expect(requests[0]?.method).toBe("GET")
		expect(requests[0]?.path).toBe("/api/terminatives?originId=c-gate")
	})

	it("a terminative revives into its class and reads its origin", async () => {
		const { client } = recordingClient(JSON.stringify(terminativeJson))

		const terminative = await client.declaratives.updateTerminative({ id: 12 }, { ignored: true })

		expect(terminative).toBeInstanceOf(Terminative)
		expect(terminative).toBeInstanceOf(Occurrence)
		expect(terminative?.originId).toBe("J1")
		expect(terminative?.originKind).toBe("objective")
		expect(terminative?.origin?.title).toBe("Deliver")
		expect(terminative?.ignored).toBe(true)
		expect(terminative?.resolution).toBe(TerminativeResolution.Pending)
	})

	it("a checkpoint's terminative reads as the checkpoint's", async () => {
		const { client } = recordingClient(JSON.stringify({
			...terminativeJson,
			objectiveId: null,
			objective: null,
			checkpointId: "c-gate",
			checkpoint: { "@type": "Checkpoint", id: "c-gate", title: "Gate" },
			recurrenceOwnerUid: "c-gate"
		}))

		const terminative = await client.declaratives.updateTerminative({ originId: "c-gate" }, {})

		expect(terminative?.originId).toBe("c-gate")
		expect(terminative?.originKind).toBe("checkpoint")
		expect(terminative?.origin?.title).toBe("Gate")
	})
})
