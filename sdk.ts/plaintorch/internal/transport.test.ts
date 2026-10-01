import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { PlaintorchCoreClient } from "../coreClient"
import { FetchPlaintorchCoreTransport } from "./transport"

const baseUrl = "http://127.0.0.1:43118"
const objectives = [{ id: "O1" }]

describe("fetch transport", () => {
	// A real `Response`, not a double: its body is a one-shot stream, which is the trap these guard.
	beforeEach(() => {
		vi.stubGlobal("fetch", async () => new Response(JSON.stringify(objectives)))
	})

	afterEach(() => {
		vi.unstubAllGlobals()
	})

	it("serves a response's body to every read", async () => {
		const response = await new FetchPlaintorchCoreTransport({ baseUrl }).send({ method: "GET", path: "/api/objectives" })

		expect(await response?.text()).toBe(JSON.stringify(objectives))
		expect(await response?.text()).toBe(JSON.stringify(objectives))
	})

	it("answers the client's JSON reads, though the client reads each response twice", async () => {
		const client = new PlaintorchCoreClient({ baseUrl })

		await expect(client.getJson("/api/objectives")).resolves.toEqual(objectives)
	})
})
