/**
 * Contracts shared by the main process, the preload bridge, and the renderer.
 *
 * The core-side shapes mirror `core/Plaintorch/Hosting`: the JSON lines a spawned core writes to stdout and the
 * payload of `/healthz`. The shell-side shapes are what the tray and the status window render.
 */

/** The lifecycle phase of the hosted core, as `PlaintorchHostPhase` serializes it. */
export type CorePhase = "Starting" | "Idle" | "Activating" | "Active" | "Failed" | "Stopping"

/** One line of the spawn-mode stdout stream. */
export type CoreEvent =
	| { event: "hello", pid: number, profile: string, endpoint: string, loopback?: string }
	| { event: "status", phase: CorePhase, message?: string, vault?: string, sweeping?: boolean, at: string }
	| { event: "listening", endpoint: string, loopback?: string }
	| { event: "stopping" }
	| { event: "already-running", endpoint: string, mode?: string, vault?: string }

/** The `/healthz` payload. */
export interface CoreHealth {
	status: string
	mode: "idle" | "active"
	activeVault?: string
	phase: CorePhase
	message?: string
	vault?: string
	sweeping?: boolean
	endpoint: string
	since: string
}

/** Which package this shell was built as. */
export type ShellFlavor = "standalone" | "client"

/** How the shell currently relates to a core process. */
export type CoreAttachment =
	/** The standalone flavour has not started its core yet. */
	| "starting"
	/** The standalone flavour spawned the core and supervises it. */
	| "spawned"
	/** A core owned by something else answers on the profile's endpoint. */
	| "attached"
	/** Nothing answers on the profile's endpoint. */
	| "absent"
	/** The spawned core exited and is not being restarted. */
	| "exited"

/** Everything a status surface needs, pushed from the main process on every change. */
export interface ShellStatus {
	flavor: ShellFlavor
	version: string
	profile: string
	endpoint: string
	logsPath: string
	attachment: CoreAttachment
	phase: CorePhase
	/** Whether the core's startup sweep is still running (Active and serving meanwhile). A status surface holds its splash while true. */
	sweeping: boolean
	message?: string
	vault?: string
	activeVaultSetting?: string
	pid?: number
	exitCode?: number
	autostart: boolean
	updateState: UpdateState
}

/** The updater's progress, for the status window and the tray. */
export type UpdateState =
	| { kind: "idle" }
	| { kind: "unavailable", reason: string }
	| { kind: "checking" }
	| { kind: "none", version: string }
	| { kind: "available", version: string }
	| { kind: "downloading", percent: number }
	| { kind: "ready", version: string }
	| { kind: "error", message: string }

/** The per-user `config.json` keys this shell reads and writes. The core owns `ActiveVaultPath`. */
export interface UserConfiguration {
	ActiveVaultPath?: string | null
	shell?: ShellConfiguration
	[key: string]: unknown
}

/** The shell's own keys inside `config.json`. */
export interface ShellConfiguration {
	/** Whether the shell registered itself to start at login. */
	autostart?: boolean
	/** Whether to show the splash while the core starts. Defaults to true. */
	splash?: boolean
}

/** A request the renderer asks the main process to send to the core, mirroring the SDK transport. */
export interface BridgeRequest {
	method: "GET" | "POST" | "PUT" | "DELETE"
	path: string
	body?: unknown
	headers?: Record<string, string>
}

/** The answer to a {@link BridgeRequest}; `undefined` on the SDK side means the core was unreachable. */
export interface BridgeResponse {
	ok: boolean
	status: number
	text: string
	/** The response headers the SDK reads (`x-note-ready`), keyed in lower case. */
	headers: Record<string, string>
}

/** One line of a long-lived core response (the change feed), pushed from the main process to the renderer. */
export interface BridgeStreamLine {
	/** The stream's id, as the opening document allocated it. */
	id: string
	line: string
}

/** What a window's own title bar draws from the window it frames. */
export interface WindowFrameState {
	/** Maximized: the maximize button shows the restore glyph. */
	maximized: boolean
	/** The window has the focus; the title bar dims without it, as a system one does. */
	focused: boolean
	/** Full screen: the title bar steps aside. */
	fullScreen: boolean
}

/**
 * The URL scheme vault files are served under to the renderer (`plaintorch-media://vault/<vault-relative path>`), by
 * the main process from the vault the core serves.
 */
export const mediaScheme = "plaintorch-media"

/**
 * The extension of a saved global planning context — a planning graph file (PEP102). SIPA's
 * `GLOBAL_CONTEXT_EXTENSION`, restated here for the main process, which does not bundle the UI; the installer's file
 * association (`installer/electron-builder.yml`) names it too.
 */
export const planningFileExtension = "p7tpx"

/** A planning window's file, as the main process last read it. */
export interface PlanningFileSnapshot {
	/** The file's absolute path. */
	path: string
	/** The file's name without its extension, for the window's title. */
	name: string
	/** The file's text, or `undefined` while there is no file at the path: it was moved or deleted elsewhere. */
	text?: string
}

/**
 * What saving a new planning file came to: the path it was saved to, as the user knows it (vault-relative inside the
 * served vault, absolute elsewhere), or why it could not be saved.
 */
export type PlanningSaveOutcome = { path: string } | { error: string }

/** IPC channel names, in one place so the preload and the main process cannot drift. */
export const ipc = {
	getStatus: "shell:get-status",
	status: "shell:status",
	activateVault: "shell:activate-vault",
	deactivateVault: "shell:deactivate-vault",
	setAutostart: "shell:set-autostart",
	openLogs: "shell:open-logs",
	openStatus: "shell:open-status",
	openBriefing: "shell:open-briefing",
	restartCore: "shell:restart-core",
	checkForUpdates: "shell:check-for-updates",
	installUpdate: "shell:install-update",
	closeSplash: "shell:close-splash",
	quit: "shell:quit",
	coreSend: "core:send",
	coreStreamOpen: "core:stream-open",
	coreStreamLine: "core:stream-line",
	coreStreamEnd: "core:stream-end",
	coreStreamClose: "core:stream-close",
	frameGetState: "frame:get-state",
	frameState: "frame:state",
	frameMinimize: "frame:minimize",
	frameToggleMaximize: "frame:toggle-maximize",
	frameClose: "frame:close",
	planningRead: "planning:read",
	planningWrite: "planning:write",
	planningChanged: "planning:changed",
	planningSaveAs: "planning:save-as"
} as const

/** The API the preload exposes on `window.plaintorch`. */
export interface PlaintorchBridge {
	getStatus(): Promise<ShellStatus>
	onStatus(listener: (status: ShellStatus) => void): () => void
	activateVault(): Promise<void>
	deactivateVault(): Promise<void>
	setAutostart(enabled: boolean): Promise<void>
	openLogs(): Promise<void>
	openStatus(): Promise<void>
	openBriefing(): Promise<void>
	restartCore(): Promise<void>
	checkForUpdates(): Promise<void>
	installUpdate(): Promise<void>
	closeSplash(): Promise<void>
	quit(): Promise<void>
	core: {
		send(request: BridgeRequest): Promise<BridgeResponse | undefined>
		/**
		 * Opens a long-lived GET (the change feed) and delivers its body a line at a time. Resolves the stream's id, or
		 * `undefined` when the core would not open it; `onEnd` follows the last line, however the stream ended. The
		 * stream belongs to the document that opened it and closes when that document is replaced.
		 */
		openStream(path: string, onLine: (line: string) => void, onEnd: () => void): Promise<string | undefined>
		/** Closes a stream this document opened. */
		closeStream(id: string): Promise<void>
	}
	/** The window this page runs in, for the title bar it draws itself (the status and briefing windows are frameless). */
	frame: {
		/** The platform: macOS keeps its own window buttons over the title bar, the others have the page draw them. */
		platform: NodeJS.Platform
		getState(): Promise<WindowFrameState | undefined>
		onState(listener: (state: WindowFrameState) => void): () => void
		minimize(): Promise<void>
		/** Maximizes the window, or restores it when it is maximized. */
		toggleMaximize(): Promise<void>
		close(): Promise<void>
	}
	/**
	 * Planning graph files. `read`, `write` and `onChanged` are a planning window's own file: each call acts on the file
	 * of the window that makes it, and only that file, so a window opened for no file reads `undefined` and writes
	 * nothing. `saveAs` makes a new one from any window.
	 */
	planning: {
		read(): Promise<PlanningFileSnapshot | undefined>
		/** Replaces the file's text. Writes are coalesced and land shortly after; closing the window lands the last one. */
		write(text: string): Promise<void>
		/** The file changed on disk from elsewhere — another editor, Obsidian, a sync — or went missing, or came back. */
		onChanged(listener: (snapshot: PlanningFileSnapshot) => void): () => void
		/**
		 * Asks where to save a new planning file — a save dialog, modal to this window, starting in the served vault —
		 * writes `text` there and opens the file in a planning window. `undefined` when the dialog was cancelled.
		 */
		saveAs(text: string): Promise<PlanningSaveOutcome | undefined>
	}
}
