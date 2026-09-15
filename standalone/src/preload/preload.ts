import { contextBridge, ipcRenderer } from "electron"
import { ipc, type BridgeRequest, type BridgeResponse, type PlaintorchBridge, type ShellStatus } from "../shared/contracts"

/**
 * The only door between the sandboxed renderer and the main process. Both windows share it: the splash uses the
 * status subscription, the status window everything, including the core transport the renderer's SDK client
 * sends through.
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
	restartCore: () => ipcRenderer.invoke(ipc.restartCore) as Promise<void>,
	checkForUpdates: () => ipcRenderer.invoke(ipc.checkForUpdates) as Promise<void>,
	installUpdate: () => ipcRenderer.invoke(ipc.installUpdate) as Promise<void>,
	closeSplash: () => ipcRenderer.invoke(ipc.closeSplash) as Promise<void>,
	quit: () => ipcRenderer.invoke(ipc.quit) as Promise<void>,
	core: {
		send: (request: BridgeRequest) => ipcRenderer.invoke(ipc.coreSend, request) as Promise<BridgeResponse | undefined>
	}
}

contextBridge.exposeInMainWorld("plaintorch", bridge)
