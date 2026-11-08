import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { PlaintorchCoreClient } from "../coreClient"
import { FetchPlaintorchCoreTransport, toLines } from "./transport"

// A stream arrives in chunks cut wherever the transport happened to cut them. `toLines` is what lets the
// change feed read whole lines regardless.

async function* chunks(...parts: string[]): AsyncIterable<string> {
	for (const part of parts) {
		yield part
	}
}

async function collect(...parts: string[]): Promise<string[]> {
	const lines: string[] = []
	for await (const line of toLines(chunks(...parts))) {
		lines.push(line)
	}

	return lines
}

describe("toLines", () => {
	it("yields the lines of a chunk carrying several", async () => {
		expect(await collect("a\nb\nc\n")).toEqual(["a", "b", "c"])
	})

	it("reassembles a line split across chunks", async () => {
		expect(await collect("da", "ta: {\"id\"", ":1}\n", "\n")).toEqual(["data: {\"id\":1}", ""])
	})

	it("keeps empty lines, which are what end an event", async () => {
		expect(await collect("data: x\n\n", "data: y\n\n")).toEqual(["data: x", "", "data: y", ""])
	})

	it("strips the carriage return of a CRLF line ending, even when the pair is split", async () => {
		expect(await collect("a\r\nb\r", "\nc\r\n")).toEqual(["a", "b", "c"])
	})

	it("yields a trailing partial line when the stream ends without a newline", async () => {
		expect(await collect("a\nb")).toEqual(["a", "b"])
		expect(await collect("a\n")).toEqual(["a"])
		expect(await collect()).toEqual([])
	})
})

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

	it("answers the client's JSON reads", async () => {
		const client = new PlaintorchCoreClient({ baseUrl })

		await expect(client.getJson("/api/objectives")).resolves.toEqual(objectives)
	})
})
