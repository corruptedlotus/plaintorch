import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "./internal/transport"

/** A transport that answers every request 200 with an empty body and an optional X-Note-Ready header. */
function transportWith(noteReadyHeader?: string): PlaintorchCoreTransport {
	return {
		async send(_request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse> {
			return {
				ok: true,
				status: 200,
				async text() {
					return "{}"
				},
				header(name) {
					return name.toLowerCase() === "x-note-ready" ? noteReadyHeader : undefined
				}
			}
		}
	}
}

describe("core client note readiness (PEP110)", () => {
	it("reports a write pending when the response carries X-Note-Ready: false", async () => {
		const client = new PlaintorchCoreClient({ transports: [transportWith("false")] })

		await client.putForJson("/api/objectives/O1", {})

		expect(client.lastWriteNotePending).toBe(true)
	})

	it("reports a write ready when the header is absent", async () => {
		const client = new PlaintorchCoreClient({ transports: [transportWith(undefined)] })

		await client.putForJson("/api/objectives/O1", {})

		expect(client.lastWriteNotePending).toBe(false)
	})

	it("leaves the flag for the write before it — a following read does not clear it", async () => {
		const client = new PlaintorchCoreClient({ transports: [transportWith("false")] })

		await client.putForJson("/api/objectives/O1", {})
		await client.getJson("/api/objectives/O1")

		expect(client.lastWriteNotePending).toBe(true)
	})
})
