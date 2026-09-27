import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "@pleiades/sdk/plaintorch"
import type { PlaintorchBridge } from "../shared/contracts"

/**
 * The SDK transport of the shell's windows. Every request crosses the preload bridge to the main process, which sends
 * it over the profile's pipe or socket, so the renderer stays sandboxed and the Node SDK is never bundled here.
 *
 * It honours the whole transport contract: the body travels as JSON (as the socket transport would write it, and so
 * that nothing a structured clone rejects ever reaches the IPC), the response exposes the headers the SDK reads —
 * without `x-note-ready` a write would throw after the core applied it — and {@link stream} relays the change feed
 * line by line.
 */
export class BridgeTransport implements PlaintorchCoreTransport {
	public constructor(private readonly bridge: PlaintorchBridge) { }

	public async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
		const response = await this.bridge.core.send({
			method: request.method,
			path: request.path,
			body: request.body === undefined ? undefined : JSON.parse(JSON.stringify(request.body)),
			headers: request.headers
		})
		return response === undefined
			? undefined
			: {
				ok: response.ok,
				status: response.status,
				text: async () => response.text,
				header: name => response.headers[name.toLowerCase()]
			}
	}

	public async stream(request: PlaintorchCoreRequest, signal: AbortSignal): Promise<AsyncIterable<string> | undefined> {
		const queue: string[] = []
		let ended = false
		let wake: (() => void) | undefined
		const id = await this.bridge.core.openStream(request.path,
			line => {
				queue.push(line)
				wake?.()
			},
			() => {
				ended = true
				wake?.()
			})
		if (id === undefined) {
			return undefined
		}

		const bridge = this.bridge
		signal.addEventListener("abort", () => void bridge.core.closeStream(id), { once: true })
		return (async function* () {
			try {
				for (;;) {
					const line = queue.shift()
					if (line !== undefined) {
						yield line
						continue
					}

					if (ended || signal.aborted) {
						return
					}

					await new Promise<void>(resolve => wake = resolve)
					wake = undefined
				}
			}
			finally {
				if (!ended) {
					void bridge.core.closeStream(id)
				}
			}
		})()
	}
}
