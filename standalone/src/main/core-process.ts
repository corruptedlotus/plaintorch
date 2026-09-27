import { spawn, type ChildProcess } from "node:child_process"
import { EventEmitter } from "node:events"
import path from "node:path"
import { app } from "electron"
import type { CoreEvent } from "../shared/contracts"
import { type CoreLocation, describeCoreLocation, locateCore } from "./core-location"
import type { UserProfile } from "./profile"

const gracefulExitTimeoutMs = 8_000
const restartDelayMs = 4_000
const maxRestartAttempts = 3
/** A core that ran at least this long before dying is considered to have been healthy, which resets the restart budget. */
const healthyRunMs = 60_000

export interface CoreProcessEvents {
	/** One parsed line of the core's stdout status stream. */
	event: [CoreEvent]
	/** The core process exited. `expected` is true when the shell asked it to stop. */
	exit: [{ code: number | null, expected: boolean, willRestart: boolean }]
	/** A line on stdout that was not JSON; surfaced so nothing is silently lost. */
	noise: [string]
	/** A line about the supervision itself: which core was chosen and its version, and any warning about that choice. */
	log: [string]
}

/**
 * Spawns and supervises the core in spawn mode.
 *
 * The child's stdout is the status channel (JSON lines, see `PlaintorchHostStatusStream`), its stdin is the
 * shutdown signal (the core stops gracefully when we close it, on every platform), and its exit is the crash
 * signal. An unexpected exit is retried a few times with a delay, then left to the user.
 */
export class CoreProcess extends EventEmitter<CoreProcessEvents> {
	private child?: ChildProcess
	private stopping = false
	private startedAt = 0
	private restartAttempts = 0
	private restartTimer?: NodeJS.Timeout
	private stdoutBuffer = ""

	public constructor(private readonly profile: UserProfile) {
		super()
	}

	/** The process id of the running core, when any. */
	public get pid(): number | undefined {
		return this.child?.pid
	}

	/** Whether a core child is alive. */
	public get isRunning(): boolean {
		return this.child !== undefined && this.child.exitCode === null && !this.child.killed
	}

	/**
	 * Where the core executable lives, resolved afresh on every start (see {@link locateCore}): `PLAINTORCH_CORE_PATH`,
	 * else the packaged `resources/core`, else, for a development run, the core project's Debug then Release build, and
	 * the installer's `core-dist` publish only when there is no build.
	 */
	public static locate(): CoreLocation | undefined {
		return locateCore({
			override: process.env.PLAINTORCH_CORE_PATH,
			packaged: app.isPackaged,
			resourcesPath: process.resourcesPath,
			appPath: app.getAppPath()
		})
	}

	/** Starts the core unless it is already running. */
	public start(): void {
		if (this.isRunning) {
			return
		}

		clearTimeout(this.restartTimer)
		const location = CoreProcess.locate()
		if (!location) {
			const message = app.isPackaged
				? "The PLAINTORCH core executable was not found beside the app."
				: "No PLAINTORCH core build was found: build the core (`dotnet build core`) or set PLAINTORCH_CORE_PATH."
			this.emit("log", message)
			this.emit("exit", { code: null, expected: false, willRestart: false })
			this.emit("event", { event: "status", phase: "Failed", message, at: new Date().toISOString() })
			return
		}

		this.emit("log", describeCoreLocation(location))
		for (const warning of location.warnings) {
			this.emit("log", `WARNING: ${warning}`)
		}

		const executable = location.executable
		this.stopping = false
		this.startedAt = Date.now()
		this.stdoutBuffer = ""
		const args = ["serve", "--spawn", "--profile", this.profile.root]
		const child = spawn(executable, args, {
			cwd: path.dirname(executable),
			stdio: ["pipe", "pipe", "pipe"],
			windowsHide: true,
			env: { ...process.env, DOTNET_NOLOGO: "1" }
		})
		this.child = child
		child.stdout?.setEncoding("utf8")
		child.stdout?.on("data", (chunk: string) => this.consumeStdout(chunk))
		child.stderr?.setEncoding("utf8")
		child.stderr?.on("data", (chunk: string) => this.emit("noise", chunk.trimEnd()))
		child.on("error", error => this.emit("noise", `core process error: ${error.message}`))
		child.on("exit", code => this.handleExit(child, code))
	}

	/** Asks the core to stop by closing its stdin, escalating to a kill if it does not exit in time. */
	public async stop(): Promise<void> {
		clearTimeout(this.restartTimer)
		const child = this.child
		if (!child || child.exitCode !== null) {
			this.child = undefined
			return
		}

		this.stopping = true
		const exited = new Promise<void>(resolve => child.once("exit", () => resolve()))
		try {
			child.stdin?.end()
		}
		catch {
			// Already gone.
		}

		const timeout = new Promise<"timeout">(resolve => setTimeout(() => resolve("timeout"), gracefulExitTimeoutMs))
		if (await Promise.race([exited, timeout]) === "timeout") {
			child.kill()
			await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 2_000))])
		}

		this.child = undefined
	}

	/** Stops the core and starts it again, resetting the restart budget. */
	public async restart(): Promise<void> {
		await this.stop()
		this.restartAttempts = 0
		this.start()
	}

	private consumeStdout(chunk: string): void {
		this.stdoutBuffer += chunk
		let newline = this.stdoutBuffer.indexOf("\n")
		while (newline >= 0) {
			const line = this.stdoutBuffer.slice(0, newline).replace(/\r$/, "").trim()
			this.stdoutBuffer = this.stdoutBuffer.slice(newline + 1)
			if (line) {
				this.dispatchLine(line)
			}

			newline = this.stdoutBuffer.indexOf("\n")
		}
	}

	private dispatchLine(line: string): void {
		let parsed: unknown
		try {
			parsed = JSON.parse(line)
		}
		catch {
			this.emit("noise", line)
			return
		}

		if (parsed && typeof parsed === "object" && typeof (parsed as { event?: unknown }).event === "string") {
			this.emit("event", parsed as CoreEvent)
		}
		else {
			this.emit("noise", line)
		}
	}

	private handleExit(child: ChildProcess, code: number | null): void {
		if (this.child !== child) {
			return
		}

		this.child = undefined
		const expected = this.stopping || code === 3
		if (Date.now() - this.startedAt > healthyRunMs) {
			this.restartAttempts = 0
		}

		const willRestart = !expected && this.restartAttempts < maxRestartAttempts
		this.emit("exit", { code, expected, willRestart })
		if (willRestart) {
			this.restartAttempts += 1
			this.restartTimer = setTimeout(() => this.start(), restartDelayMs)
		}
	}
}
