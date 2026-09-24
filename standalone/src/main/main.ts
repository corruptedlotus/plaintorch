import { app, dialog, ipcMain, shell as electronShell, nativeTheme } from "electron"
import { mkdirSync } from "node:fs"
import { ipc, type BridgeRequest, type BridgeStreamLine, type ShellStatus } from "../shared/contracts"
import { setAutostartEnabled } from "./autostart"
import { hostsCore } from "./flavor"
import { handleMediaScheme, registerMediaScheme } from "./media-protocol"
import { resolveUserProfile } from "./profile"
import { Shell } from "./shell"
import { ShellTray } from "./tray"
import { ShellWindows } from "./windows"

/**
 * The main process: one instance per user, a tray icon, a splash while the core comes up, the briefing (the SIPA
 * UI) and a status window on demand, and a clean stop of the core on quit. Everything stateful lives in {@link Shell}; this file only wires
 * Electron's lifecycle and the surfaces to it.
 */

function readArgument(name: string): string | undefined {
	const index = process.argv.indexOf(name)
	return index >= 0 ? process.argv[index + 1] : undefined
}

const launchedHidden = process.argv.includes("--hidden")
const profile = resolveUserProfile(readArgument("--profile"))
mkdirSync(profile.root, { recursive: true })

registerMediaScheme()

// One product identity for both flavours, matching the installer's appId, so the taskbar groups the windows under the
// installed shortcut. A second launch on the same profile only surfaces the running one; two different profiles (a dev
// build beside an installed one) are two different apps and coexist.
app.setAppUserModelId("pleiades.plaintorch")
if (!app.requestSingleInstanceLock({ profile: profile.root })) {
	app.quit()
}
else {
	void run()
}

async function run(): Promise<void> {
	const shell = new Shell(profile)
	const windows = new ShellWindows()
	let quitting = false

	const quit = async () => {
		if (quitting) {
			return
		}

		quitting = true
		tray.destroy()
		windows.destroyAll()
		await shell.shutdown()
		app.exit(0)
	}

	const activateVault = async () => {
		const result = await dialog.showOpenDialog(windows.statusWindow ?? (null as unknown as Electron.BrowserWindow), {
			title: "Activate a PLAINTORCH vault",
			message: "Choose an initialized PLAINTORCH vault folder",
			properties: ["openDirectory"]
		})
		const chosen = result.filePaths[0]
		if (!result.canceled && chosen) {
			shell.activateVault(chosen)
		}
	}

	const tray = new ShellTray({
		openBriefing: () => windows.showBriefing(),
		openStatus: () => windows.showStatus(),
		activateVault: () => void activateVault(),
		deactivateVault: () => shell.deactivateVault(),
		setAutostart: enabled => {
			setAutostartEnabled(enabled)
			shell.configuration.updateShell(section => {
				section.autostart = enabled
			})
			shell.publish()
		},
		openLogs: () => {
			mkdirSync(profile.logsPath, { recursive: true })
			void electronShell.openPath(profile.logsPath)
		},
		restartCore: () => void shell.restartCore(),
		checkForUpdates: () => void shell.updater.check(),
		installUpdate: () => void shell.updater.downloadAndInstall(),
		quit: () => void quit()
	})

	let briefingShownAfterSplash = false
	const publish = (status: ShellStatus) => {
		tray.update(status)
		windows.broadcast(status)
		// The splash outlives the startup phases only for a failure, which it displays until dismissed.
		if (windows.isSplashOpen && !shell.isCoreStarting && status.phase !== "Failed") {
			setTimeout(() => {
				windows.closeSplash()
				// A visible launch ends in the main window once the core is up; a login launch stays in the tray.
				if (!launchedHidden && !briefingShownAfterSplash) {
					briefingShownAfterSplash = true
					windows.showBriefing()
				}
			}, 900)
		}
	}

	ipcMain.handle(ipc.getStatus, () => shell.status)
	ipcMain.handle(ipc.activateVault, () => activateVault())
	ipcMain.handle(ipc.deactivateVault, () => shell.deactivateVault())
	ipcMain.handle(ipc.setAutostart, (_, enabled: boolean) => {
		setAutostartEnabled(enabled)
		shell.configuration.updateShell(section => {
			section.autostart = enabled
		})
		shell.publish()
	})
	ipcMain.handle(ipc.openLogs, () => {
		mkdirSync(profile.logsPath, { recursive: true })
		return electronShell.openPath(profile.logsPath)
	})
	ipcMain.handle(ipc.openStatus, () => windows.showStatus())
	ipcMain.handle(ipc.openBriefing, () => windows.showBriefing())
	ipcMain.handle(ipc.restartCore, () => shell.restartCore())
	ipcMain.handle(ipc.checkForUpdates, () => shell.updater.check())
	ipcMain.handle(ipc.installUpdate, () => shell.updater.downloadAndInstall())
	ipcMain.handle(ipc.closeSplash, () => windows.closeSplash())
	ipcMain.handle(ipc.quit, () => quit())
	ipcMain.handle(ipc.coreSend, (_, request: BridgeRequest) => shell.transport.send(request))

	// Long-lived core responses (the change feed), relayed line by line to the window that opened them and closed when
	// it asks, navigates or goes away — else the core would keep a feed subscription open for a window long gone.
	const streams = new Map<string, AbortController>()
	ipcMain.handle(ipc.coreStreamOpen, async (event, id: number, path: string) => {
		const sender = event.sender
		const key = `${sender.id}:${id}`
		const controller = new AbortController()
		const lines = await shell.transport.stream(path, controller.signal)
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

	app.on("second-instance", () => windows.showBriefing())
	// A tray app stays alive with no windows.
	app.on("window-all-closed", () => { })
	app.on("before-quit", event => {
		if (!quitting) {
			event.preventDefault()
			void quit()
		}
	})

	await app.whenReady()
	handleMediaScheme(() => shell.status.vault)
	tray.create()
	nativeTheme.on("updated", () => tray.updateIcon())
	shell.on("status", publish)
	shell.on("log", line => console.warn(`[core] ${line}`))

	const showSplash = hostsCore && shell.configuration.shell().splash !== false
	if (showSplash) {
		windows.showSplash()
	}

	if (!launchedHidden && !showSplash) {
		windows.showBriefing()
	}

	await shell.start()
}
