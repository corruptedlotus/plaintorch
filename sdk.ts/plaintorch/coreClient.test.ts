import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { PlaintorchCoreClient } from "./coreClient"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "./internal/transport"

// How the client hands a response to the store. Every body-returning request is a *read* as far as the store
// is concerned: it is stamped with the revision it was issued under, so a local change made while it was in
// flight outranks it, and it is closed however it ends, so the change ledger can be pruned.

type Thing = { id: string, title: string, status: number }

const objective = (status: number) => ({ "@type": "Objective", id: "O1", title: "one", status })

function respond(body: unknown, status = 200): PlaintorchCoreResponse & { reads: number } {
	const response = {
		ok: status >= 200 && status < 300,
		status,
		reads: 0,
		async text() {
			response.reads++
			return body === undefined ? "" : JSON.stringify(body)
		},
		header: () => undefined
	}
	return response
}

/** A transport whose every request waits for the test to answer it. */
function deferredTransport() {
	const pending: { request: PlaintorchCoreRequest, answer: (response: PlaintorchCoreResponse | undefined) => void, fail: (error: Error) => void }[] = []
	const transport: PlaintorchCoreTransport = {
		send: async (request) => await new Promise((resolve, reject) => {
			pending.push({ request, answer: resolve, fail: reject })
		})
	}
	return { transport, pending }
}

function harness() {
	const { transport, pending } = deferredTransport()
	const client = new PlaintorchCoreClient({ transports: [transport] })
	return { client, pending }
}

beforeEach(() => {
	vi.spyOn(console, "debug").mockImplementation(() => { })
})

afterEach(() => {
	vi.restoreAllMocks()
})

describe("PlaintorchCoreClient — a body-returning request is a read", () => {
	it("opens the read before the request leaves and closes it when the response lands", async () => {
		const { client, pending } = harness()

		const request = client.getJson<Thing>("/api/objectives/O1")
		expect(client.store.inFlightReadCount).toBe(1)

		pending[0]!.answer(respond(objective(5)))
		expect((await request)?.status).toBe(5)
		expect(client.store.inFlightReadCount).toBe(0)
	})

	it("absorbs the response, so the result is the store's canonical instance", async () => {
		const { client, pending } = harness()

		const request = client.getJson<Thing>("/api/objectives/O1")
		pending[0]!.answer(respond(objective(5)))

		expect(await request).toBe(client.store.peek("Objective:O1"))
	})

	it("discards a response for an identity changed locally while the request was in flight", async () => {
		const { client, pending } = harness()
		const first = client.getJson<Thing>("/api/objectives/O1")
		pending[0]!.answer(respond(objective(0)))
		const held = (await first)!

		const stale = client.getJson<Thing>("/api/objectives/O1")
		held.status = 9
		client.store.touch("Objective:O1")
		pending[1]!.answer(respond(objective(5)))

		expect(await stale).toBe(held)
		expect(held.status).toBe(9)
	})

	it("applies a response issued after the local change", async () => {
		const { client, pending } = harness()
		const first = client.getJson<Thing>("/api/objectives/O1")
		pending[0]!.answer(respond(objective(0)))
		const held = (await first)!

		held.status = 9
		client.store.touch("Objective:O1")
		const fresh = client.getJson<Thing>("/api/objectives/O1")
		pending[1]!.answer(respond(objective(5)))
		await fresh

		expect(held.status).toBe(5)
	})

	it("an authoritative read applies even over a local change made while it was in flight", async () => {
		const { client, pending } = harness()
		const first = client.getJson<Thing>("/api/objectives/O1")
		pending[0]!.answer(respond(objective(0)))
		const held = (await first)!

		const authoritative = client.getJson<Thing>("/api/objectives/O1", { authoritative: true })
		held.status = 9
		client.store.touch("Objective:O1")
		pending[1]!.answer(respond(objective(5)))
		await authoritative

		expect(held.status).toBe(5)
	})

	it("a write that returns the entity is bracketed the same way", async () => {
		for (const send of [
			(client: PlaintorchCoreClient) => client.postForJson<Thing>("/api/objectives", {}),
			(client: PlaintorchCoreClient) => client.putForJson<Thing>("/api/objectives/O1", {})
		]) {
			const { client, pending } = harness()
			const request = send(client)
			expect(client.store.inFlightReadCount).toBe(1)

			pending[0]!.answer(respond(objective(5)))
			expect(await request).toBe(client.store.peek("Objective:O1"))
			expect(client.store.inFlightReadCount).toBe(0)
		}
	})

	it("closes the read when the request finds nothing, answers empty, or the transport throws", async () => {
		const { client, pending } = harness()

		const unanswered = client.getJson<Thing>("/api/objectives/O1")
		pending[0]!.answer(undefined)
		expect(await unanswered).toBeUndefined()

		const empty = client.getJson<Thing>("/api/objectives/O1")
		pending[1]!.answer(respond(undefined))
		expect(await empty).toBeUndefined()

		const refused = client.getJson<Thing>("/api/objectives/O1")
		pending[2]!.answer(respond({ error: "no" }, 404))
		expect(await refused).toBeUndefined()

		const thrown = client.getJson<Thing>("/api/objectives/O1")
		pending[3]!.fail(new Error("pipe closed"))
		await expect(thrown).rejects.toThrow("pipe closed")

		expect(client.store.inFlightReadCount).toBe(0)
	})

	it("a settled read lets the change ledger be pruned", async () => {
		const { client, pending } = harness()
		const first = client.getJson<Thing>("/api/objectives/O1")
		pending[0]!.answer(respond(objective(0)))
		await first

		const inFlight = client.getJson<Thing>("/api/objectives/O1")
		client.store.touch("Objective:O1")
		expect(client.store.pendingChangeCount).toBe(1)

		pending[1]!.answer(respond(objective(5)))
		await inFlight
		expect(client.store.pendingChangeCount).toBe(0)
	})
})

describe("PlaintorchCoreClient — a boolean write", () => {
	it("is not a read, reports only whether the core accepted it, and still consumes the body", async () => {
		const { client, pending } = harness()

		const request = client.postJson("/api/objectives/O1/shift", {})
		expect(client.store.inFlightReadCount).toBe(0)

		const response = respond(objective(5))
		pending[0]!.answer(response)
		expect(await request).toBe(true)
		expect(response.reads).toBeGreaterThan(0)
		// The body was drained, not absorbed.
		expect(client.store.has("Objective:O1")).toBe(false)
	})

	it("reports a refusal as false", async () => {
		const { client, pending } = harness()

		const removal = client.delete("/api/objectives/O1")
		pending[0]!.answer(respond(undefined, 409))
		expect(await removal).toBe(false)

		const update = client.putJson("/api/objectives/O1", {})
		pending[1]!.answer(undefined)
		expect(await update).toBe(false)
	})
})
