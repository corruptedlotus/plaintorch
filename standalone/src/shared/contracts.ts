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
	| { event: "status", phase: CorePhase, message?: string, vault?: string, at: string }
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
}

/** IPC channel names, in one place so the preload and the main process cannot drift. */
export const ipc = {
	getStatus: "shell:get-status",
	status: "shell:status",
	activateVault: "shell:activate-vault",
	deactivateVault: "shell:deactivate-vault",
	setAutostart: "shell:set-autostart",
	openLogs: "shell:open-logs",
	openStatus: "shell:open-status",
	restartCore: "shell:restart-core",
	checkForUpdates: "shell:check-for-updates",
	installUpdate: "shell:install-update",
	closeSplash: "shell:close-splash",
	quit: "shell:quit",
	coreSend: "core:send"
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
	restartCore(): Promise<void>
	checkForUpdates(): Promise<void>
	installUpdate(): Promise<void>
	closeSplash(): Promise<void>
	quit(): Promise<void>
	core: {
		send(request: BridgeRequest): Promise<BridgeResponse | undefined>
	}
}
