import path from "node:path"
import { pathToFileURL } from "node:url"
import { BrowserWindow, screen } from "electron"
import type { ShellStatus } from "../shared/contracts"
import { ipc } from "../shared/contracts"

const splashSize = { width: 943, height: 405 }
const statusSize = { width: 720, height: 560 }

function rendererUrl(view: "splash" | "status"): string {
	// pathToFileURL gives a well-formed file:/// URL; a hand-built "file://" + a Windows path would leave the drive
	// letter parsed as a host and, depending on the platform, fail to load.
	const url = pathToFileURL(path.join(__dirname, "index.html"))
	url.hash = view
	return url.href
}

function preloadPath(): string {
	return path.join(__dirname, "preload.cjs")
}

/**
 * The two windows of the shell, both rendering the same bundle behind a different hash: the frameless splash that
 * mirrors the old startup popup, and the ordinary status window.
 */
/** How long the splash stays up at minimum, so a fast idle startup shows a graceful splash rather than a blink. */
const minimumSplashMs = 1_400

export class ShellWindows {
	private splash?: BrowserWindow
	private status?: BrowserWindow
	private latest?: ShellStatus
	private splashShownAt = 0

	/** Pushes a status to every open window; windows opened later receive it on load. */
	public broadcast(status: ShellStatus): void {
		this.latest = status
		for (const window of [this.splash, this.status]) {
			if (window && !window.isDestroyed()) {
				window.webContents.send(ipc.status, status)
			}
		}
	}

	/** Shows the splash, centred on the primary display, above everything, without a taskbar entry. */
	public showSplash(): void {
		if (this.splash && !this.splash.isDestroyed()) {
			return
		}

		const { workArea } = screen.getPrimaryDisplay()
		this.splash = new BrowserWindow({
			...splashSize,
			x: Math.round(workArea.x + (workArea.width - splashSize.width) / 2),
			y: Math.round(workArea.y + (workArea.height - splashSize.height) / 2),
			frame: false,
			transparent: true,
			resizable: false,
			movable: false,
			minimizable: false,
			maximizable: false,
			alwaysOnTop: true,
			skipTaskbar: true,
			focusable: false,
			show: false,
			hasShadow: false,
			webPreferences: this.webPreferences()
		})
		this.splashShownAt = 0
		this.splash.once("ready-to-show", () => {
			this.splash?.showInactive()
			this.splashShownAt = Date.now()
		})
		this.splash.on("closed", () => {
			this.splash = undefined
		})
		void this.splash.loadURL(rendererUrl("splash"))
	}

	/**
	 * Closes the splash when it is open, but never before it has been visible for {@link minimumSplashMs}, so a core
	 * that reaches idle in a few hundred milliseconds still shows a splash the eye can register rather than a blink.
	 */
	public closeSplash(): void {
		if (!this.splash || this.splash.isDestroyed()) {
			this.splash = undefined
			return
		}

		const shownFor = this.splashShownAt === 0 ? 0 : Date.now() - this.splashShownAt
		const remaining = minimumSplashMs - shownFor
		if (remaining > 0) {
			setTimeout(() => this.closeSplash(), remaining)
			return
		}

		this.splash.close()
		this.splash = undefined
	}

	/** Whether the splash is on screen. */
	public get isSplashOpen(): boolean {
		return this.splash !== undefined && !this.splash.isDestroyed()
	}

	/** Opens the status window, or brings the existing one forward. */
	public showStatus(): void {
		if (this.status && !this.status.isDestroyed()) {
			if (this.status.isMinimized()) {
				this.status.restore()
			}

			this.status.show()
			this.status.focus()
			return
		}

		this.status = new BrowserWindow({
			...statusSize,
			minWidth: 480,
			minHeight: 360,
			title: "PLAINTORCH",
			autoHideMenuBar: true,
			show: false,
			backgroundColor: "#1f1a22",
			icon: path.join(__dirname, "assets", "plaintorch-full.png"),
			webPreferences: this.webPreferences()
		})
		this.status.once("ready-to-show", () => this.status?.show())
		this.status.on("closed", () => {
			this.status = undefined
		})
		void this.status.loadURL(rendererUrl("status"))
	}

	/** The status window, when open. */
	public get statusWindow(): BrowserWindow | undefined {
		return this.status && !this.status.isDestroyed() ? this.status : undefined
	}

	/** Destroys every window, for quitting. */
	public destroyAll(): void {
		for (const window of [this.splash, this.status]) {
			if (window && !window.isDestroyed()) {
				window.destroy()
			}
		}

		this.splash = undefined
		this.status = undefined
	}

	private webPreferences(): Electron.WebPreferences {
		return {
			preload: preloadPath(),
			contextIsolation: true,
			nodeIntegration: false,
			sandbox: true,
			spellcheck: false
		}
	}

	/** The last status broadcast, for a window that just loaded. */
	public get latestStatus(): ShellStatus | undefined {
		return this.latest
	}
}
