import { app, BrowserWindow, dialog, ipcMain, shell as electronShell, nativeTheme } from "electron"
import { mkdirSync } from "node:fs"
import { ipc, type BridgeRequest, type ShellStatus } from "../shared/contracts"
import { setAutostartEnabled } from "./autostart"
import { relayCoreStreams } from "./core-streams"
import { hostsCore } from "./flavor"
import { handleMediaScheme, registerMediaScheme } from "./media-protocol"
import { handlePlanningWindows, planningFilesIn } from "./planning-files"
import { resolveUserProfile } from "./profile"
import { refuseRemoteFiles } from "./request-guard"
import { Shell } from "./shell"
import { ShellTray } from "./tray"
import { handleFrameControls } from "./window-frame"
import { ShellWindows } from "./windows"

/**
 * The main process: one instance per user, a tray icon, a splash while the core comes up, the briefing (the SIPA
 * UI) and a status window on demand, a planning window for each planning graph file (`.p7tpx`) the app is asked to
 * open and each canvas context popped out, and a clean stop of the core on quit. Everything stateful lives in {@link Shell}; this file only wires
 * Electron's lifecycle and the surfaces to it.
 */

function readArgument(name: string): string | undefined {
	const index = process.argv.indexOf(name)
	return index >= 0 ? process.argv[index + 1] : undefined
}

const launchedHidden = process.argv.includes("--hidden")
/** The planning graph files the launch names: opening one from the file manager launches the app with its path. */
const launchFiles = planningFilesIn(process.argv, process.cwd())
const profile = resolveUserProfile(readArgument("--profile"))
mkdirSync(profile.shellDataPath, { recursive: true })
// Electron keys its single-instance lock on the userData folder, so moving it into the profile, before the lock is
// taken and before anything reads it, is what makes one shell per profile rather than one per app name.
app.setPath("userData", profile.shellDataPath)

registerMediaScheme()

// One product identity for both flavours, matching the installer's appId, so the taskbar groups the windows under the
// installed shortcut. A second launch on the same profile only surfaces the running one; shells on different profiles
// (a dev build beside an installed one, or two dev shells with different `--profile` folders) coexist.
app.setAppUserModelId("pleiades.plaintorch")
if (!app.requestSingleInstanceLock()) {
	app.quit()
}
else {
	void run()
}

async function run(): Promise<void> {
	const shell = new Shell(profile)
	const windows = new ShellWindows()
	let quitting = false
	let splashTimer: NodeJS.Timeout | undefined

	const quit = async () => {
		if (quitting) {
			return
		}

		quitting = true
		clearTimeout(splashTimer)
		tray.destroy()
		windows.destroyAll()
		await shell.shutdown()
		app.exit(0)
	}

	let choosingVault = false
	/** Asks for a vault folder, modal to the window that asked (the tray has none); one picker at a time. */
	const activateVault = async (owner?: BrowserWindow) => {
		if (choosingVault) {
			return
		}

		choosingVault = true
		try {
			const options: Electron.OpenDialogOptions = {
				title: "Activate a PLAINTORCH vault",
				message: "Choose an initialized PLAINTORCH vault folder",
				properties: ["openDirectory"]
			}
			const parent = owner ?? windows.statusWindow
			const result = parent ? await dialog.showOpenDialog(parent, options) : await dialog.showOpenDialog(options)
			const chosen = result.filePaths[0]
			if (!result.canceled && chosen && !quitting) {
				shell.activateVault(chosen)
			}
		}
		finally {
			choosingVault = false
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

	/** Planning files waiting for the launch's windows: those the launch named, and any asked for before they show. */
	const pendingFiles = [...launchFiles]
	let launchWindowsShown = false
	/**
	 * The windows a launch ends in, once: the planning files waiting, or — for a visible launch that asked for none —
	 * the main window. A login launch stays in the tray.
	 */
	const showLaunchWindows = () => {
		if (launchWindowsShown) {
			return
		}

		launchWindowsShown = true
		const files = pendingFiles.splice(0)
		if (files.length === 0 && !launchedHidden) {
			windows.showBriefing()
		}

		for (const file of files) {
			windows.showPlanning(file)
		}
	}

	/** Opens planning files, or holds them for the launch's windows while the app is not ready or the splash is up. */
	const openPlanningFiles = (files: readonly string[]) => {
		if (!launchWindowsShown && (!app.isReady() || windows.isSplashOpen)) {
			pendingFiles.push(...files)
			return
		}

		for (const file of files) {
			windows.showPlanning(file)
		}
	}

	const publish = (status: ShellStatus) => {
		tray.update(status)
		windows.broadcast(status)
		// The splash outlives the startup phases only for a failure, which it displays until dismissed.
		if (windows.isSplashOpen && !shell.isCoreStarting && status.phase !== "Failed") {
			clearTimeout(splashTimer)
			splashTimer = setTimeout(() => {
				if (quitting) {
					return
				}

				windows.closeSplash()
				showLaunchWindows()
			}, 900)
		}
	}

	ipcMain.handle(ipc.getStatus, () => shell.status)
	ipcMain.handle(ipc.activateVault, event => activateVault(BrowserWindow.fromWebContents(event.sender) ?? undefined))
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

	relayCoreStreams(shell.transport)
	handleFrameControls()
	handlePlanningWindows({
		contentOf: contents => windows.planningContentOf(contents),
		write: (contents, text) => windows.writePlanning(contents, text),
		fileAt: filePath => windows.planningFileAt(filePath),
		servedVault: () => shell.servedVault,
		saved: (contents, filePath) => windows.savedPlanning(contents, filePath),
		popOut: context => windows.showPlanningContext(context)
	})

	// A second launch hands its command line over: the planning files it names open here, and a launch naming none
	// brings the main window forward.
	app.on("second-instance", (_, argv, workingDirectory) => {
		if (quitting) {
			return
		}

		const files = planningFilesIn(argv, workingDirectory)
		if (files.length === 0) {
			windows.showBriefing()
		}
		else {
			openPlanningFiles(files)
		}
	})
	// macOS hands a file over as an event instead, before the app is ready when the file launched it.
	app.on("open-file", (event, file) => {
		event.preventDefault()
		if (!quitting) {
			openPlanningFiles(planningFilesIn([file], process.cwd()))
		}
	})
	// A tray app stays alive with no windows.
	app.on("window-all-closed", () => { })
	app.on("before-quit", event => {
		if (!quitting) {
			event.preventDefault()
			void quit()
		}
	})

	await app.whenReady()
	refuseRemoteFiles()
	handleMediaScheme(() => shell.servedVault)
	tray.create()
	nativeTheme.on("updated", () => tray.updateIcon())
	shell.on("status", publish)
	shell.on("log", line => console.warn(`[core] ${line}`))

	const showSplash = hostsCore && shell.configuration.shell().splash !== false
	if (showSplash) {
		windows.showSplash()
	}
	else {
		showLaunchWindows()
	}

	await shell.start()
}
