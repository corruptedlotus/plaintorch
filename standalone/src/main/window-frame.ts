import { BrowserWindow, ipcMain, type BrowserWindowConstructorOptions, type IpcMainInvokeEvent } from "electron"
import { ipc, type WindowFrameState } from "../shared/contracts"

/**
 * The frame options of a window that draws its own title bar (`p7t-window-frame` in the page). Windows and Linux get
 * no system frame at all — the window keeps its resizable edges, its shadow and its snapping — and the page draws the
 * buttons. macOS keeps its traffic lights, centred in the page's title bar, which leaves them room.
 */
export function customFrame(): BrowserWindowConstructorOptions {
	return process.platform === "darwin"
		? { titleBarStyle: "hidden", trafficLightPosition: { x: 12, y: 10 } }
		: { frame: false }
}

function frameState(window: BrowserWindow): WindowFrameState {
	return { maximized: window.isMaximized(), focused: window.isFocused(), fullScreen: window.isFullScreen() }
}

/** Keeps a framed window's title bar told of the state it draws: maximized, focused, full screen. */
export function relayFrameState(window: BrowserWindow): void {
	const send = () => {
		if (!window.isDestroyed()) {
			window.webContents.send(ipc.frameState, frameState(window))
		}
	}

	window.on("maximize", send)
	window.on("unmaximize", send)
	window.on("restore", send)
	window.on("focus", send)
	window.on("blur", send)
	window.on("enter-full-screen", send)
	window.on("leave-full-screen", send)
}

/** Answers a title bar's buttons: each acts on the window whose page asked. */
export function handleFrameControls(): void {
	const owner = (event: IpcMainInvokeEvent) => BrowserWindow.fromWebContents(event.sender) ?? undefined

	ipcMain.handle(ipc.frameGetState, event => {
		const window = owner(event)
		return window ? frameState(window) : undefined
	})
	ipcMain.handle(ipc.frameMinimize, event => {
		const window = owner(event)
		if (window?.isMinimizable()) {
			window.minimize()
		}
	})
	ipcMain.handle(ipc.frameToggleMaximize, event => {
		const window = owner(event)
		if (!window?.isMaximizable()) {
			return
		}

		if (window.isMaximized()) {
			window.unmaximize()
		}
		else {
			window.maximize()
		}
	})
	ipcMain.handle(ipc.frameClose, event => {
		owner(event)?.close()
	})
}
