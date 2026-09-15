import { EventEmitter } from "node:events"
import { app } from "electron"
import type { CoreAttachment, CoreEvent, CorePhase, ShellStatus } from "../shared/contracts"
import { isAutostartEnabled } from "./autostart"
import { UserConfigurationStore } from "./config"
import { CoreProcess } from "./core-process"
import { CoreTransport } from "./core-transport"
import { flavor, hostsCore } from "./flavor"
import type { UserProfile } from "./profile"
import { ShellUpdater } from "./updater"

const attachedPollMs = 5_000

/**
 * The shell's single source of truth: it folds the core's status stream (standalone) or health probes (client), the
 * shared configuration, the autostart registration, and the updater into one {@link ShellStatus} that the tray and
 * every window render. Anything that changes the world goes through here too, so every surface updates together.
 */
export class Shell extends EventEmitter<{ status: [ShellStatus], log: [string] }> {
	public readonly configuration: UserConfigurationStore
	public readonly transport: CoreTransport
	public readonly updater = new ShellUpdater()
	private readonly core?: CoreProcess
	private attachment: CoreAttachment = hostsCore ? "starting" : "absent"
	private phase: CorePhase = "Starting"
	private message?: string
	private vault?: string
	private exitCode?: number
	private pollTimer?: NodeJS.Timeout
	private quitting = false

	public constructor(public readonly profile: UserProfile) {
		super()
		this.configuration = new UserConfigurationStore(profile)
		this.transport = new CoreTransport(profile)
		if (hostsCore) {
			this.core = new CoreProcess(profile)
			this.core.on("event", event => this.applyCoreEvent(event))
			this.core.on("exit", ({ code, expected, willRestart }) => {
				this.exitCode = code ?? undefined
				if (expected && code === 3) {
					// Another core already owns the profile; attach to it instead of fighting over the pipe.
					this.startPolling()
					return
				}

				this.attachment = willRestart ? "starting" : "exited"
				if (!this.quitting) {
					this.phase = "Failed"
					this.message = willRestart
						? `The core exited unexpectedly (code ${code ?? "?"}). Restarting...`
						: `The core exited (code ${code ?? "?"}). Restart it from the tray.`
				}

				this.publish()
			})
			this.core.on("noise", line => this.emit("log", line))
		}
	}

	/** The current snapshot. */
	public get status(): ShellStatus {
		return {
			flavor,
			version: app.getVersion(),
			profile: this.profile.root,
			endpoint: this.profile.endpointDisplay,
			logsPath: this.profile.logsPath,
			attachment: this.attachment,
			phase: this.phase,
			message: this.message,
			vault: this.vault,
			activeVaultSetting: this.configuration.load().ActiveVaultPath ?? undefined,
			pid: this.core?.pid,
			exitCode: this.exitCode,
			autostart: isAutostartEnabled(),
			updateState: this.updater.current
		}
	}

	/** Whether the core is in a phase worth a splash: it is still coming up. */
	public get isCoreStarting(): boolean {
		return this.attachment === "starting" || this.phase === "Starting" || this.phase === "Activating"
	}

	/** Brings the shell up: watches the configuration, starts or finds the core, prepares the updater. */
	public async start(): Promise<void> {
		this.configuration.startWatching()
		this.configuration.on("changed", () => this.publish())
		this.updater.on("state", () => this.publish())
		await this.updater.initialize(() => this.stopCore())

		if (this.core) {
			this.core.start()
		}
		else {
			this.startPolling()
		}

		this.publish()
	}

	/** Stops the core (standalone) and every timer. */
	public async shutdown(): Promise<void> {
		this.quitting = true
		clearInterval(this.pollTimer)
		this.configuration.stopWatching()
		await this.stopCore()
	}

	/** Writes the active vault into the shared configuration; the core reconciles from there. */
	public activateVault(vaultPath: string): void {
		this.configuration.update(configuration => {
			configuration.ActiveVaultPath = vaultPath
		})
		this.publish()
	}

	/** Clears the active vault; a running core returns to idle. */
	public deactivateVault(): void {
		this.configuration.update(configuration => {
			configuration.ActiveVaultPath = null
		})
		this.publish()
	}

	/** Restarts the spawned core (standalone only). */
	public async restartCore(): Promise<void> {
		if (!this.core) {
			return
		}

		clearInterval(this.pollTimer)
		this.attachment = "starting"
		this.phase = "Starting"
		this.message = "Restarting PLAINTORCH core..."
		this.exitCode = undefined
		this.publish()
		await this.core.restart()
	}

	/** Re-emits the current status to every listener. */
	public publish(): void {
		this.emit("status", this.status)
	}

	private async stopCore(): Promise<void> {
		if (!this.core?.isRunning) {
			return
		}

		this.phase = "Stopping"
		this.message = "Stopping PLAINTORCH core..."
		this.publish()
		await this.core.stop()
	}

	private applyCoreEvent(event: CoreEvent): void {
		switch (event.event) {
			case "hello":
				this.attachment = "spawned"
				this.exitCode = undefined
				break
			case "status":
				this.phase = event.phase
				this.message = event.message
				this.vault = event.vault
				break
			case "listening":
				this.attachment = "spawned"
				break
			case "stopping":
				this.phase = "Stopping"
				break
			case "already-running":
				this.attachment = "attached"
				this.vault = event.vault
				break
		}

		this.publish()
	}

	/**
	 * Client flavour, or a standalone that found another core on the profile: follow the core through its health
	 * endpoint instead of a status stream.
	 */
	private startPolling(): void {
		clearInterval(this.pollTimer)
		const poll = async () => {
			const health = await this.transport.probeHealth()
			const previous = `${this.attachment}|${this.phase}|${this.message}|${this.vault}`
			if (health) {
				this.attachment = "attached"
				this.phase = health.phase
				this.message = health.message
				this.vault = health.vault ?? health.activeVault
			}
			else {
				this.attachment = "absent"
				this.phase = "Failed"
				this.message = hostsCore
					? "No PLAINTORCH core answers on this profile. Restart it from the tray."
					: "No PLAINTORCH core is running for this profile. Start the standalone app or the console daemon."
				this.vault = undefined
			}

			if (previous !== `${this.attachment}|${this.phase}|${this.message}|${this.vault}`) {
				this.publish()
			}
		}

		void poll()
		this.pollTimer = setInterval(() => void poll(), attachedPollMs)
	}
}
