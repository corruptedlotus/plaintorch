import { ipcMain } from "electron"
import { ipc, type BridgeStreamLine } from "../shared/contracts"
import type { CoreTransport } from "./core-transport"

/**
 * Relays long-lived core responses (the change feed) to the windows that open them, line by line over the preload
 * bridge. A stream is closed when its window asks, reloads or goes away — otherwise the core would keep a feed
 * subscription open for a window long gone.
 */
export function relayCoreStreams(transport: CoreTransport): void {
	const streams = new Map<string, AbortController>()

	ipcMain.handle(ipc.coreStreamOpen, async (event, id: number, path: string) => {
		const sender = event.sender
		const key = `${sender.id}:${id}`
		const controller = new AbortController()
		const lines = await transport.stream(path, controller.signal)
		if (!lines || sender.isDestroyed()) {
			controller.abort()
			return false
		}

		streams.set(key, controller)
		const abort = () => controller.abort()
		// A reload replaces the document that owned the stream; a same-document (hash) navigation does not.
		const onNavigation = (details: Electron.Event<Electron.WebContentsDidStartNavigationEventParams>) => {
			if (details.isMainFrame && !details.isSameDocument) {
				abort()
			}
		}
		sender.once("destroyed", abort)
		sender.on("did-start-navigation", onNavigation)
		void (async () => {
			try {
				for await (const line of lines) {
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
				controller.abort()
				streams.delete(key)
				if (!sender.isDestroyed()) {
					sender.removeListener("destroyed", abort)
					sender.removeListener("did-start-navigation", onNavigation)
					sender.send(ipc.coreStreamEnd, id)
				}
			}
		})()
		return true
	})

	ipcMain.handle(ipc.coreStreamClose, (event, id: number) => {
		streams.get(`${event.sender.id}:${id}`)?.abort()
	})
}
