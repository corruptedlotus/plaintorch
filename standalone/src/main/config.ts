import { EventEmitter } from "node:events"
import { existsSync, mkdirSync, readFileSync, renameSync, watch, writeFileSync, type FSWatcher } from "node:fs"
import path from "node:path"
import type { ShellConfiguration, UserConfiguration } from "../shared/contracts"
import type { UserProfile } from "./profile"

/**
 * The shared per-user `config.json`, read and written directly.
 *
 * The core owns `ActiveVaultPath` and reconciles whenever the file changes, so activating a vault from the shell is
 * just a write here. Every write goes to a sibling temp file and is renamed into place, so neither the core's
 * watcher nor this one ever reads a half-written file. Keys this shell does not model are preserved verbatim, the
 * same courtesy the core extends to the shell's keys.
 */
export class UserConfigurationStore extends EventEmitter<{ changed: [UserConfiguration] }> {
	private watcher?: FSWatcher
	private debounce?: NodeJS.Timeout

	public constructor(private readonly profile: UserProfile) {
		super()
	}

	/** Reads the current configuration; a missing or unreadable file reads as empty. */
	public load(): UserConfiguration {
		try {
			if (!existsSync(this.profile.configurationPath)) {
				return {}
			}

			const parsed: unknown = JSON.parse(readFileSync(this.profile.configurationPath, "utf8"))
			return parsed && typeof parsed === "object" ? parsed as UserConfiguration : {}
		}
		catch {
			return {}
		}
	}

	/** Writes the configuration atomically. */
	public save(configuration: UserConfiguration): void {
		mkdirSync(this.profile.root, { recursive: true })
		const temporaryPath = `${this.profile.configurationPath}.shell.tmp`
		writeFileSync(temporaryPath, `${JSON.stringify(configuration, null, "\t")}\n`, "utf8")
		renameSync(temporaryPath, this.profile.configurationPath)
	}

	/** Applies a change to the loaded configuration and saves it. */
	public update(mutate: (configuration: UserConfiguration) => void): UserConfiguration {
		const configuration = this.load()
		mutate(configuration)
		this.save(configuration)
		return configuration
	}

	/** The shell's own section, never undefined. */
	public shell(): ShellConfiguration {
		return this.load().shell ?? {}
	}

	/** Updates the shell's own section. */
	public updateShell(mutate: (shell: ShellConfiguration) => void): void {
		this.update(configuration => {
			const shell = configuration.shell ?? {}
			mutate(shell)
			configuration.shell = shell
		})
	}

	/** Starts watching the file, emitting `changed` (debounced) with the fresh content. */
	public startWatching(): void {
		if (this.watcher) {
			return
		}

		mkdirSync(this.profile.root, { recursive: true })
		try {
			this.watcher = watch(this.profile.root, (_, fileName) => {
				if (fileName && fileName !== path.basename(this.profile.configurationPath)) {
					return
				}

				clearTimeout(this.debounce)
				this.debounce = setTimeout(() => this.emit("changed", this.load()), 150)
			})
		}
		catch {
			// Without a watcher the shell still reads the file on demand; only live reflection of CLI activations is lost.
		}
	}

	/** Stops watching the file. */
	public stopWatching(): void {
		clearTimeout(this.debounce)
		this.watcher?.close()
		this.watcher = undefined
	}
}
