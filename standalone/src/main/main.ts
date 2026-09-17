import { app, dialog, ipcMain, shell as electronShell, nativeTheme } from "electron"
import { mkdirSync } from "node:fs"
import { ipc, type BridgeRequest, type ShellStatus } from "../shared/contracts"
import { setAutostartEnabled } from "./autostart"
import { flavor, hostsCore } from "./flavor"
import { resolveUserProfile } from "./profile"
import { Shell } from "./shell"
import { ShellTray } from "./tray"
import { ShellWindows } from "./windows"

/**
 * The main process: one instance per user, a tray icon, a splash while the core comes up, a status window on
 * demand, and a clean stop of the core on quit. Everything stateful lives in {@link Shell}; this file only wires
 * Electron's lifecycle and the surfaces to it.
 */

function readArgument(name: string): string | undefined {
	const index = process.argv.indexOf(name)
	return index >= 0 ? process.argv[index + 1] : undefined
}

const launchedHidden = process.argv.includes("--hidden")
const profile = resolveUserProfile(readArgument("--profile"))
mkdirSync(profile.root, { recursive: true })

// A second launch of the same flavour on the same profile only surfaces the running one. Two different profiles
// (a dev build beside an installed one) are two different apps and coexist.
app.setAppUserModelId(flavor === "client" ? "pleiades.plaintorch.client" : "pleiades.plaintorch")
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

	const publish = (status: ShellStatus) => {
		tray.update(status)
		windows.broadcast(status)
		// The splash outlives the startup phases only for a failure, which it displays until dismissed.
		if (windows.isSplashOpen && !shell.isCoreStarting && status.phase !== "Failed") {
			setTimeout(() => windows.closeSplash(), 900)
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
	ipcMain.handle(ipc.restartCore, () => shell.restartCore())
	ipcMain.handle(ipc.checkForUpdates, () => shell.updater.check())
	ipcMain.handle(ipc.installUpdate, () => shell.updater.downloadAndInstall())
	ipcMain.handle(ipc.closeSplash, () => windows.closeSplash())
	ipcMain.handle(ipc.quit, () => quit())
	ipcMain.handle(ipc.coreSend, (_, request: BridgeRequest) => shell.transport.send(request))

	app.on("second-instance", () => windows.showStatus())
	// A tray app stays alive with no windows.
	app.on("window-all-closed", () => { })
	app.on("before-quit", event => {
		if (!quitting) {
			event.preventDefault()
			void quit()
		}
	})

	await app.whenReady()
	tray.create()
	nativeTheme.on("updated", () => tray.updateIcon())
	shell.on("status", publish)
	shell.on("log", line => console.warn(`[core] ${line}`))

	const showSplash = hostsCore && shell.configuration.shell().splash !== false
	if (showSplash) {
		windows.showSplash()
	}

	if (!launchedHidden && !showSplash) {
		windows.showStatus()
	}

	await shell.start()
}
