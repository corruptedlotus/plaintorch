import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import { OnrushSlot } from "./onrush/models"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "./internal/transport"

/** A client whose one transport answers every request with the given status and body, recording what it was sent. */
function answeringClient(status: number, body = "{}") {
	const requests: PlaintorchCoreRequest[] = []
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse> {
			requests.push(request)
			return {
				ok: status >= 200 && status < 300,
				status,
				async text() {
					return body
				},
				header() {
					return undefined
				}
			}
		}
	}

	return { client: new PlaintorchCoreClient({ transports: [transport] }), requests }
}

/**
 * Joining an onrush reports the core's answer (PEP102 patch 10). A refused or unknown route used to read as a success,
 * because the client reports a failed write as `undefined` and the membership calls compared it with `null`: a core
 * too old to know the route answered 404 and the UI still said the member had joined.
 */
describe("onrush membership outcome", () => {
	it("a stellar directive's join reports success only when the core accepts it", async () => {
		const accepted = answeringClient(200, '{"id":"D1","onrushSprintId":"x0000"}')
		expect(await accepted.client.directives.addToOnrush("D1", OnrushSlot.Planning)).toBe(true)
		expect(accepted.requests[0]?.path).toBe("/api/directives/stellar/D1/onrush")
		expect(accepted.requests[0]?.body).toEqual({ slot: OnrushSlot.Planning })

		expect(await answeringClient(404).client.directives.addToOnrush("D1", OnrushSlot.Active)).toBe(false)
		expect(await answeringClient(400).client.directives.addToOnrush("D1", "x9999")).toBe(false)
	})

	it("an objective's join reports success only when the core accepts it", async () => {
		const accepted = answeringClient(200, '{"id":"O1","onrushSprintId":"x0000"}')
		expect(await accepted.client.objectives.addToOnrush("O1", OnrushSlot.Planning)).toBe(true)
		expect(accepted.requests[0]?.path).toBe("/api/objectives/O1/onrush")

		expect(await answeringClient(404).client.objectives.addToOnrush("O1", OnrushSlot.Active)).toBe(false)
		expect(await answeringClient(500).client.objectives.addToOnrush("O1", "x9999")).toBe(false)
	})
})
