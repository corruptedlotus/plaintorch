import { contextBridge, ipcRenderer } from "electron"
import { ipc, type BridgeRequest, type BridgeResponse, type BridgeStreamLine, type PlaintorchBridge, type ShellStatus, type WindowFrameState } from "../shared/contracts"

/** The streams this document has open, by id: where their lines and their end go. */
const streams = new Map<string, { onLine: (line: string) => void, onEnd: () => void }>()
// Ids are allocated here, not in the main process, so a stream's listeners are in place before its first line can
// arrive — a line sent before the renderer learnt an id would otherwise be lost. Each carries this document's own
// key: a reload starts the counter over, and a stream the previous document opened must never be taken for one of
// this document's.
const documentKey = crypto.randomUUID()
let nextStreamId = 1

ipcRenderer.on(ipc.coreStreamLine, (_: Electron.IpcRendererEvent, { id, line }: BridgeStreamLine) => streams.get(id)?.onLine(line))
ipcRenderer.on(ipc.coreStreamEnd, (_: Electron.IpcRendererEvent, id: string) => {
	const stream = streams.get(id)
	streams.delete(id)
	stream?.onEnd()
})

/**
 * The only door between the sandboxed renderer and the main process. Every window shares it: the splash uses the
 * status subscription, the status and briefing windows everything, including the core transport the renderer's SDK
 * client sends through, the change feed it streams, and the window their own title bars drive.
 */
const bridge: PlaintorchBridge = {
	getStatus: () => ipcRenderer.invoke(ipc.getStatus) as Promise<ShellStatus>,
	onStatus: listener => {
		const handler = (_: Electron.IpcRendererEvent, status: ShellStatus) => listener(status)
		ipcRenderer.on(ipc.status, handler)
		return () => ipcRenderer.removeListener(ipc.status, handler)
	},
	activateVault: () => ipcRenderer.invoke(ipc.activateVault) as Promise<void>,
	deactivateVault: () => ipcRenderer.invoke(ipc.deactivateVault) as Promise<void>,
	setAutostart: enabled => ipcRenderer.invoke(ipc.setAutostart, enabled) as Promise<void>,
	openLogs: () => ipcRenderer.invoke(ipc.openLogs) as Promise<void>,
	openStatus: () => ipcRenderer.invoke(ipc.openStatus) as Promise<void>,
	openBriefing: () => ipcRenderer.invoke(ipc.openBriefing) as Promise<void>,
	restartCore: () => ipcRenderer.invoke(ipc.restartCore) as Promise<void>,
	checkForUpdates: () => ipcRenderer.invoke(ipc.checkForUpdates) as Promise<void>,
	installUpdate: () => ipcRenderer.invoke(ipc.installUpdate) as Promise<void>,
	closeSplash: () => ipcRenderer.invoke(ipc.closeSplash) as Promise<void>,
	quit: () => ipcRenderer.invoke(ipc.quit) as Promise<void>,
	core: {
		send: (request: BridgeRequest) => ipcRenderer.invoke(ipc.coreSend, request) as Promise<BridgeResponse | undefined>,
		openStream: async (path, onLine, onEnd) => {
			const id = `${documentKey}:${nextStreamId++}`
			streams.set(id, { onLine, onEnd })
			let opened = false
			try {
				opened = await ipcRenderer.invoke(ipc.coreStreamOpen, id, path) === true
			}
			finally {
				if (!opened) {
					streams.delete(id)
				}
			}

			return opened ? id : undefined
		},
		closeStream: async id => {
			await ipcRenderer.invoke(ipc.coreStreamClose, id)
		}
	},
	frame: {
		platform: process.platform,
		getState: () => ipcRenderer.invoke(ipc.frameGetState) as Promise<WindowFrameState | undefined>,
		onState: listener => {
			const handler = (_: Electron.IpcRendererEvent, state: WindowFrameState) => listener(state)
			ipcRenderer.on(ipc.frameState, handler)
			return () => ipcRenderer.removeListener(ipc.frameState, handler)
		},
		minimize: () => ipcRenderer.invoke(ipc.frameMinimize) as Promise<void>,
		toggleMaximize: () => ipcRenderer.invoke(ipc.frameToggleMaximize) as Promise<void>,
		close: () => ipcRenderer.invoke(ipc.frameClose) as Promise<void>
	}
}

contextBridge.exposeInMainWorld("plaintorch", bridge)
