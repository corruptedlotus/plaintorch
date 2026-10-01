import path from "node:path"
import { pathToFileURL } from "node:url"
import { BrowserWindow, screen, shell, type WebContents } from "electron"
import type { OnrushCanvasMode, PlanningPopOut, PlanningWindowContent, ShellStatus } from "../shared/contracts"
import { ipc } from "../shared/contracts"
import { PlanningFile, planningFileKey } from "./planning-files"
import { customFrame, relayFrameState } from "./window-frame"

const splashSize = { width: 943, height: 405 }
const statusSize = { width: 720, height: 560 }
const briefingSize = { width: 1180, height: 820 }
const planningSize = { width: 1180, height: 820 }

/** What a planning window shows: a file, a global context not saved to one yet, or an onrush. */
type PlanningSubject =
	| { readonly kind: "file", readonly file: PlanningFile }
	| { readonly kind: "unsaved", text: string }
	| { readonly kind: "onrush", readonly mode: OnrushCanvasMode }

/** A planning window and what it shows. */
interface PlanningWindow {
	readonly window: BrowserWindow
	/** The page's `webContents` id, kept because a destroyed window no longer gives out its `webContents`. */
	readonly contentsId: number
	/** The window's key among the planning windows; an unsaved window takes its file's once saved. */
	key: string
	subject: PlanningSubject
}

/** What a planning window's page is told it shows. */
function contentOf(subject: PlanningSubject): PlanningWindowContent {
	switch (subject.kind) {
		case "file": return subject.file.snapshot
		case "unsaved": return { kind: "unsaved", text: subject.text }
		case "onrush": return { kind: "onrush", mode: subject.mode }
	}
}

/**
 * A window's page. Each has a bundle of its own: the splash stays small, so it shows the moment the app starts, while
 * the status window, the briefing and the planning windows carry the SIPA UI.
 */
function pageUrl(page: "splash.html" | "status.html" | "briefing.html" | "planning.html"): string {
	// pathToFileURL gives a well-formed file:/// URL; a hand-built "file://" + a Windows path would leave the drive
	// letter parsed as a host and, depending on the platform, fail to load.
	return pathToFileURL(path.join(__dirname, page)).href
}

/** The window icon: the full-colour PLAINTORCH mark. */
function windowIcon(): string {
	return path.join(__dirname, "assets", "plaintorch-full.png")
}

function preloadPath(): string {
	return path.join(__dirname, "preload.cjs")
}

/** How long the splash stays up at minimum, so a fast idle startup shows a graceful splash rather than a blink. */
const minimumSplashMs = 1_400

/**
 * The shell's windows, each on a page of its own: the frameless splash that mirrors the old startup popup, the status
 * window, the briefing — the SIPA UI — and the planning windows: one for each planning graph file (`.p7tpx`) opened,
 * one for each onrush popped out of a canvas, and one for each global context popped out unsaved. The status,
 * briefing and planning windows draw their own title bars ({@link customFrame}).
 */
export class ShellWindows {
	private splash?: BrowserWindow
	private status?: BrowserWindow
	private briefing?: BrowserWindow
	/**
	 * The planning windows, by key: a file's {@link planningFileKey}, `onrush:<mode>`, or `unsaved:<n>` — one window
	 * per file and per onrush.
	 */
	private readonly planning = new Map<string, PlanningWindow>()
	/** Counts the unsaved planning windows opened, for their keys. */
	private unsavedCount = 0
	private latest?: ShellStatus
	private splashShownAt = 0

	/** Pushes a status to every open window; windows opened later receive it on load. */
	public broadcast(status: ShellStatus): void {
		this.latest = status
		for (const window of [this.splash, this.status, this.briefing, ...this.planningWindows]) {
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
		this.guard(this.splash)
		this.splashShownAt = 0
		this.splash.once("ready-to-show", () => {
			this.splash?.showInactive()
			this.splashShownAt = Date.now()
		})
		this.splash.on("closed", () => {
			this.splash = undefined
		})
		void this.splash.loadURL(pageUrl("splash.html"))
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
			...customFrame(),
			minWidth: 480,
			minHeight: 360,
			title: "PLAINTORCH",
			show: false,
			backgroundColor: "#1f1a22",
			icon: windowIcon(),
			webPreferences: this.webPreferences()
		})
		this.guard(this.status)
		relayFrameState(this.status)
		this.status.once("ready-to-show", () => this.status?.show())
		this.status.on("closed", () => {
			this.status = undefined
		})
		void this.status.loadURL(pageUrl("status.html"))
	}

	/** Opens the briefing window, or brings the existing one forward. */
	public showBriefing(): void {
		if (this.briefing && !this.briefing.isDestroyed()) {
			if (this.briefing.isMinimized()) {
				this.briefing.restore()
			}

			this.briefing.show()
			this.briefing.focus()
			return
		}

		this.briefing = new BrowserWindow({
			...briefingSize,
			...customFrame(),
			minWidth: 720,
			minHeight: 520,
			title: "PLAINTORCH Briefing",
			show: false,
			backgroundColor: "#1f1a22",
			icon: windowIcon(),
			webPreferences: this.webPreferences()
		})
		this.guard(this.briefing)
		relayFrameState(this.briefing)
		this.briefing.once("ready-to-show", () => this.briefing?.show())
		this.briefing.on("closed", () => {
			this.briefing = undefined
		})
		void this.briefing.loadURL(pageUrl("briefing.html"))
	}

	/**
	 * Opens a planning graph file in a window of its own — the file-backed dependency canvas the Obsidian plugin opens
	 * the file into — or brings forward the window it is already open in.
	 */
	public showPlanning(filePath: string): void {
		this.openPlanning(planningFileKey(filePath), window => ({ kind: "file", file: this.planningFile(window, filePath) }))
	}

	/**
	 * Opens a canvas context in a planning window, as a planning graph file opens. An onrush has one window, brought
	 * forward when open; a global context, popped out unsaved, gets a new window every time — it has no identity
	 * until it is saved to a file.
	 */
	public showPlanningContext(context: PlanningPopOut): void {
		if (context.mode === "global") {
			this.openPlanning(`unsaved:${++this.unsavedCount}`, () => ({ kind: "unsaved", text: context.text }))
		}
		else {
			const mode = context.mode
			this.openPlanning(`onrush:${mode}`, () => ({ kind: "onrush", mode }))
		}
	}

	/** What the planning window a page runs in shows; undefined for any other page. */
	public planningContentOf(contents: WebContents): PlanningWindowContent | undefined {
		const entry = this.planningEntryOf(contents)
		return entry && contentOf(entry.subject)
	}

	/**
	 * Takes the global context text a planning window's page hands over: written to its file, or kept as its unsaved
	 * context, which a reload of the page reads back.
	 */
	public writePlanning(contents: WebContents, text: string): void {
		const subject = this.planningEntryOf(contents)?.subject
		if (subject?.kind === "file") {
			subject.file.write(text)
		}
		else if (subject?.kind === "unsaved") {
			subject.text = text
		}
	}

	/**
	 * A page saved a new planning file. The unsaved planning window that saved it becomes that file's window — unless
	 * another window has the file open, which comes forward instead while the unsaved one closes, its context saved.
	 * Saved from any other page, the file opens in a window of its own.
	 */
	public savedPlanning(contents: WebContents, filePath: string): void {
		const entry = this.planningEntryOf(contents)
		if (entry?.subject.kind !== "unsaved") {
			this.showPlanning(filePath)
			return
		}

		const key = planningFileKey(filePath)
		const open = this.planning.get(key)
		if (open && !open.window.isDestroyed()) {
			this.showPlanning(filePath)
			entry.window.close()
			return
		}

		this.planning.delete(entry.key)
		entry.key = key
		entry.subject = { kind: "file", file: this.planningFile(entry.window, filePath) }
		this.planning.set(key, entry)
		entry.window.webContents.send(ipc.planningChanged, contentOf(entry.subject))
	}

	/** The file of the planning window open on a path, if one is. */
	public planningFileAt(filePath: string): PlanningFile | undefined {
		const open = this.planning.get(planningFileKey(filePath))
		return open && !open.window.isDestroyed() && open.subject.kind === "file" ? open.subject.file : undefined
	}

	/** The status window, when open. */
	public get statusWindow(): BrowserWindow | undefined {
		return this.status && !this.status.isDestroyed() ? this.status : undefined
	}

	/** Destroys every window, for quitting. A planning window's last write lands first. */
	public destroyAll(): void {
		for (const { subject } of this.planning.values()) {
			if (subject.kind === "file") {
				subject.file.close()
			}
		}

		for (const window of [this.splash, this.status, this.briefing, ...this.planningWindows]) {
			if (window && !window.isDestroyed()) {
				window.destroy()
			}
		}

		this.splash = undefined
		this.status = undefined
		this.briefing = undefined
		this.planning.clear()
	}

	private get planningWindows(): BrowserWindow[] {
		return Array.from(this.planning.values(), entry => entry.window)
	}

	/** Brings forward the planning window under `key`, or opens one showing what `subjectFor` gives it. */
	private openPlanning(key: string, subjectFor: (window: BrowserWindow) => PlanningSubject): void {
		const open = this.planning.get(key)
		if (open && !open.window.isDestroyed()) {
			if (open.window.isMinimized()) {
				open.window.restore()
			}

			open.window.show()
			open.window.focus()
			return
		}

		const window = new BrowserWindow({
			...planningSize,
			...customFrame(),
			minWidth: 640,
			minHeight: 420,
			title: "PLAINTORCH Planning",
			show: false,
			backgroundColor: "#1f1a22",
			icon: windowIcon(),
			webPreferences: this.webPreferences()
		})
		const entry: PlanningWindow = { window, contentsId: window.webContents.id, key, subject: subjectFor(window) }
		this.planning.set(key, entry)
		this.guard(window)
		relayFrameState(window)
		window.once("ready-to-show", () => window.show())
		window.on("closed", () => {
			if (entry.subject.kind === "file") {
				entry.subject.file.close()
			}

			// The key, not the one the window opened under: an unsaved window that was saved is now its file's.
			if (this.planning.get(entry.key) === entry) {
				this.planning.delete(entry.key)
			}
		})
		void window.loadURL(pageUrl("planning.html"))
	}

	/** Opens a planning window's file, which tells the window's page of every change it hears from elsewhere. */
	private planningFile(window: BrowserWindow, filePath: string): PlanningFile {
		return new PlanningFile(path.resolve(filePath), snapshot => {
			if (!window.isDestroyed()) {
				window.webContents.send(ipc.planningChanged, snapshot)
			}
		})
	}

	private planningEntryOf(contents: WebContents): PlanningWindow | undefined {
		for (const entry of this.planning.values()) {
			if (entry.contentsId === contents.id) {
				return entry
			}
		}

		return undefined
	}

	/**
	 * Keeps a window on its own page: it never navigates away, and a link that would open a window opens in the
	 * system browser instead when it is a web link, and nowhere otherwise.
	 */
	private guard(window: BrowserWindow): void {
		window.webContents.setWindowOpenHandler(({ url }) => {
			if (/^https?:/i.test(url)) {
				void shell.openExternal(url)
			}

			return { action: "deny" }
		})
		window.webContents.on("will-navigate", event => event.preventDefault())
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
