import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import { TimeframeInclusion } from "./directives/models"
import { PolarisExecutivePlanningMode } from "./polaris/models"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "./internal/transport"

/** One request as the core would receive it: the body JSON round-tripped, so undefined keys drop and null stays. */
interface WireRequest {
	method: PlaintorchCoreRequest["method"]
	path: string
	body: unknown
}

/**
 * A transport that records every request the way it crosses the wire and answers 200 with a fixed body. The body is
 * JSON round-tripped exactly as the real transports stringify it, which is what gives the tri-state affinity its
 * meaning: an omitted (undefined) key never reaches the core, an explicit `null` does.
 */
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

describe("creation affinity tri-state (PEP100 patch 2)", () => {
	it("planExecutive omits the affinity key for auto", async () => {
		const { client, requests } = recordingClient()

		await client.polaris.planExecutive({ mode: PolarisExecutivePlanningMode.FromObjective, objectiveId: "O1", affinityTimeframeId: undefined })

		expect(requests[0]?.method).toBe("POST")
		expect(requests[0]?.path).toBe("/api/polaris/current/executives/plan")
		expect(requests[0]?.body).not.toHaveProperty("affinityTimeframeId")
	})

	it("planExecutive sends null for an explicit none", async () => {
		const { client, requests } = recordingClient()

		await client.polaris.planExecutive({ mode: PolarisExecutivePlanningMode.FromObjective, objectiveId: "O1", affinityTimeframeId: null })

		expect(requests[0]?.body).toHaveProperty("affinityTimeframeId", null)
	})

	it("planExecutive sends the id of a picked timeframe", async () => {
		const { client, requests } = recordingClient()

		await client.polaris.planExecutive({ mode: PolarisExecutivePlanningMode.FromObjective, objectiveId: "O1", affinityTimeframeId: 7 })

		expect(requests[0]?.body).toHaveProperty("affinityTimeframeId", 7)
	})

	it("addDecreeExecutive omits the affinity key for auto", async () => {
		const { client, requests } = recordingClient()

		await client.polaris.addDecreeExecutive({ decreeId: "D1", affinityTimeframeId: undefined })

		expect(requests[0]?.method).toBe("POST")
		expect(requests[0]?.path).toBe("/api/polaris/current/decrees")
		expect(requests[0]?.body).toEqual({ decreeId: "D1" })
	})

	it("addDecreeExecutive sends null for an explicit none", async () => {
		const { client, requests } = recordingClient()

		await client.polaris.addDecreeExecutive({ decreeId: "D1", affinityTimeframeId: null })

		expect(requests[0]?.body).toEqual({ decreeId: "D1", affinityTimeframeId: null })
	})

	it("addDecreeExecutive sends the id of a picked timeframe", async () => {
		const { client, requests } = recordingClient()

		await client.polaris.addDecreeExecutive({ decreeId: "D1", affinityTimeframeId: 7 })

		expect(requests[0]?.body).toEqual({ decreeId: "D1", affinityTimeframeId: 7 })
	})
})

describe("active timeframes (PEP100 patch 2)", () => {
	it("reads the core's active listing", async () => {
		const { client, requests } = recordingClient("[]")

		await client.directives.listActiveTimeframes()

		expect(requests[0]?.method).toBe("GET")
		expect(requests[0]?.path).toBe("/api/timeframes/active")
	})

	it("answers an empty list for an empty response", async () => {
		const { client } = recordingClient("")

		expect(await client.directives.listActiveTimeframes()).toEqual([])
	})
})

describe("active timeframes record (PEP100 patch 3)", () => {
	const listing = JSON.stringify([{ id: 7, title: "Morning", exclusive: false }])
	const activeReads = (requests: WireRequest[]) => requests.filter(request => request.path === "/api/timeframes/active").length

	it("resolves the core's active listing", async () => {
		const { client, requests } = recordingClient(listing)

		const timeframes = await client.repos.activeTimeframes.get()

		expect(timeframes?.map(timeframe => timeframe.id)).toEqual([7])
		expect(activeReads(requests)).toBe(1)
	})

	it("coalesces concurrent re-reads into one request", async () => {
		const { client, requests } = recordingClient(listing)

		await Promise.all(Array.from({ length: 12 }, () => client.repos.activeTimeframes.revalidateIfObserved()))
		expect(activeReads(requests)).toBe(0)

		const unsubscribe = client.repos.activeTimeframes.subscribe("", () => { })
		await Promise.all(Array.from({ length: 12 }, () => client.repos.activeTimeframes.revalidateIfObserved()))
		unsubscribe()

		expect(activeReads(requests)).toBe(1)
	})

	it("is revalidated with the other observed records", async () => {
		const { client, requests } = recordingClient(listing)
		const unsubscribe = client.repos.activeTimeframes.subscribe("", () => { })

		await client.repos.revalidateObservedRecords()
		unsubscribe()
		await client.repos.revalidateObservedRecords()

		expect(activeReads(requests)).toBe(1)
	})
})

describe("directive availability (PEP100 patch 2)", () => {
	it("sets an availability by timeframe id", async () => {
		const { client, requests } = recordingClient()

		await client.directives.setAvailability("Some Directive/1", 7)

		expect(requests[0]?.method).toBe("PUT")
		expect(requests[0]?.path).toBe("/api/directives/Some%20Directive%2F1/availability")
		expect(requests[0]?.body).toEqual({ timeframeId: 7 })
	})

	it("clears an availability with an explicit null", async () => {
		const { client, requests } = recordingClient()

		await client.directives.setAvailability("D1", null)

		expect(requests[0]?.path).toBe("/api/directives/D1/availability")
		expect(requests[0]?.body).toEqual({ timeframeId: null })
	})

	it("mirrors the core's appended Availability inclusion ordinal", () => {
		expect(TimeframeInclusion.Availability).toBe(2)
	})
})
