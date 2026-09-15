import { EventEmitter } from "node:events"
import { app } from "electron"
import type { UpdateState } from "../shared/contracts"

/**
 * Wraps electron-updater for the "download the new installer and run it" flow.
 *
 * Nothing downloads on its own: a check reports availability, the user asks for the download, and installing quits
 * the shell (which stops the core, releasing its executable) before the installer runs. The feed URL and the
 * installer format come from `installer/electron-builder.yml`. A development run has no updater at all.
 */
export class ShellUpdater extends EventEmitter<{ state: [UpdateState] }> {
	private state: UpdateState = { kind: "idle" }
	private updater?: import("electron-updater").AppUpdater
	private beforeInstall?: () => Promise<void>

	/** The last reported state. */
	public get current(): UpdateState {
		return this.state
	}

	/** Prepares the updater. `beforeInstall` runs before the installer launches, to stop the core. */
	public async initialize(beforeInstall: () => Promise<void>): Promise<void> {
		this.beforeInstall = beforeInstall
		if (!app.isPackaged) {
			this.set({ kind: "unavailable", reason: "Updates are only available in a packaged build." })
			return
		}

		try {
			const { autoUpdater } = await import("electron-updater")
			autoUpdater.autoDownload = false
			autoUpdater.autoInstallOnAppQuit = false
			autoUpdater.on("checking-for-update", () => this.set({ kind: "checking" }))
			autoUpdater.on("update-not-available", info => this.set({ kind: "none", version: info.version }))
			autoUpdater.on("update-available", info => this.set({ kind: "available", version: info.version }))
			autoUpdater.on("download-progress", progress => this.set({ kind: "downloading", percent: Math.round(progress.percent) }))
			autoUpdater.on("update-downloaded", info => this.set({ kind: "ready", version: info.version }))
			autoUpdater.on("error", error => this.set({ kind: "error", message: error.message }))
			this.updater = autoUpdater
		}
		catch (error) {
			this.set({ kind: "unavailable", reason: error instanceof Error ? error.message : String(error) })
		}
	}

	/** Checks the feed; the result arrives as a state change. */
	public async check(): Promise<void> {
		if (!this.updater) {
			return
		}

		try {
			await this.updater.checkForUpdates()
		}
		catch (error) {
			this.set({ kind: "error", message: error instanceof Error ? error.message : String(error) })
		}
	}

	/** Downloads the available update, then runs the installer after the core has been stopped. */
	public async downloadAndInstall(): Promise<void> {
		if (!this.updater) {
			return
		}

		if (this.state.kind === "available") {
			try {
				await this.updater.downloadUpdate()
			}
			catch (error) {
				this.set({ kind: "error", message: error instanceof Error ? error.message : String(error) })
				return
			}
		}

		if (this.state.kind !== "ready") {
			return
		}

		await this.beforeInstall?.()
		this.updater.quitAndInstall(false, true)
	}

	private set(state: UpdateState): void {
		this.state = state
		this.emit("state", state)
	}
}
