import { vi } from "vitest"
import { PlaintorchCoreClient } from "../coreClient"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "../internal/transport"

// Shared fakes for the suites about PLAINTORCH's repositories: a real client over a transport the test scripts,
// so a suite exercises the same path a response takes in the application — transport, client, reviver, store.
// Not a `.test.ts` file, so Vitest does not run it as a suite.

/** What the scripted core does with a request: a body it answers with, a status it refuses with, or silence. */
export type Scripted =
	| { body: unknown }
	| { status: number, body?: unknown }
	| "silence"

/**
 * A client whose core is a table of paths the test edits. A path the table does not name is answered with a
 * 404, as the core answers for an entity it does not have.
 */
export function scriptedClient(routes: Record<string, Scripted> = {}) {
	const requests: PlaintorchCoreRequest[] = []
	const transport: PlaintorchCoreTransport = {
		async send(request) {
			requests.push(request)
			const scripted = routes[request.path] ?? { status: 404 }
			if (scripted === "silence") {
				return undefined
			}

			const status = "status" in scripted ? scripted.status : 200
			return respond(scripted.body, status)
		}
	}

	vi.spyOn(console, "debug").mockImplementation(() => { })
	const client = new PlaintorchCoreClient({ transports: [transport] })
	return {
		client,
		routes,
		requests,
		/** The paths requested so far, in order. */
		paths: (): string[] => requests.map((request) => request.path)
	}
}

function respond(body: unknown, status: number): PlaintorchCoreResponse {
	return {
		ok: status >= 200 && status < 300,
		status,
		async text() {
			return body === undefined ? "" : JSON.stringify(body)
		},
		header: () => undefined
	}
}

/** An objective as the core serializes one. */
export function objective(id: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
	return { "@type": "Objective", id, title: id, status: 0, ...extra }
}
