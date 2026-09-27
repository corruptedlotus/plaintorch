import { ipcMain } from "electron"
import { ipc, type BridgeStreamLine } from "../shared/contracts"
import type { CoreTransport } from "./core-transport"

/** What a window may stream: a core API path in printable ASCII, which is all the HTTP request line accepts. */
const streamPath = /^\/api\/[\x21-\x7e]*$/

/** A stream id as the preload allocates it: the document's own key, then a counter. */
const streamId = /^[\w-]{1,64}:\d{1,9}$/

/**
 * Relays long-lived core responses (the change feed) to the windows that open them, line by line over the preload
 * bridge. A stream is closed when its window asks, when the document that opened it is replaced (a reload), or when
 * the window goes away — otherwise the core would keep a feed subscription open for a document long gone.
 *
 * Stream ids carry the opening document's key, so a stream never outlives or crosses into another document: its lines
 * reach only the document that asked, even one still opening when a reload replaced it.
 */
export function relayCoreStreams(transport: CoreTransport): void {
	const streams = new Map<string, AbortController>()

	ipcMain.handle(ipc.coreStreamOpen, async (event, id: unknown, path: unknown) => {
		if (typeof id !== "string" || !streamId.test(id) || typeof path !== "string" || !streamPath.test(path)) {
			return false
		}

		const sender = event.sender
		const key = `${sender.id}:${id}`
		streams.get(key)?.abort()
		const controller = new AbortController()
		streams.set(key, controller)
		const abort = () => controller.abort()
		// Listening from before the open, so a reload while the core is slow to answer still closes it. A committed
		// navigation, not a started one: the window guard cancels every navigation but a reload, and one it cancelled
		// leaves the document — and its feed — in place.
		sender.once("destroyed", abort)
		sender.on("did-navigate", abort)
		sender.on("render-process-gone", abort)
		const release = () => {
			controller.abort()
			if (streams.get(key) === controller) {
				streams.delete(key)
			}

			if (!sender.isDestroyed()) {
				sender.removeListener("destroyed", abort)
				sender.removeListener("did-navigate", abort)
				sender.removeListener("render-process-gone", abort)
			}
		}

		let lines: AsyncIterable<string> | undefined
		try {
			lines = await transport.stream(path, controller.signal)
		}
		catch {
			lines = undefined
		}

		if (!lines || controller.signal.aborted || sender.isDestroyed()) {
			release()
			return false
		}

		const opened = lines
		void (async () => {
			try {
				for await (const line of opened) {
					if (sender.isDestroyed() || controller.signal.aborted) {
						break
					}

					sender.send(ipc.coreStreamLine, { id, line } satisfies BridgeStreamLine)
				}
			}
			catch {
				// A dropped stream simply ends; the feed reconnects on its own.
			}
			finally {
				release()
				if (!sender.isDestroyed()) {
					sender.send(ipc.coreStreamEnd, id)
				}
			}
		})()
		return true
	})

	ipcMain.handle(ipc.coreStreamClose, (event, id: unknown) => {
		if (typeof id === "string") {
			streams.get(`${event.sender.id}:${id}`)?.abort()
		}
	})
}
