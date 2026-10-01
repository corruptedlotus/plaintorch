import { spawn } from "node:child_process"
import { existsSync } from "node:fs"
import path from "node:path"
import { CoreProcess } from "./core-process"
import type { UserProfile } from "./profile"

/** The settings file whose presence makes a folder a PLAINTORCH vault: the core's `VaultOptions.DefaultSettingsFileName`. */
const vaultSettingsFileName = ".plaintorch"

/** A generous bound on a one-shot `init`: a new vault takes seconds, but a slow disk or a cold start can stretch that. */
const initializationTimeoutMs = 120_000

/** How a {@link initializeVault} ended: the folder is a vault now, or why it is not. */
export type VaultInitialization = { initialized: true } | { initialized: false, reason: string }

/** Whether a folder is an initialized PLAINTORCH vault, by the same test the core applies before it serves one. */
export function isVaultInitialized(folder: string): boolean {
	return existsSync(path.join(folder, vaultSettingsFileName))
}

/**
 * Initializes a folder as a PLAINTORCH vault by running the core's own `init` against it (`plaintorch init --vault
 * <folder> --profile <root>`), so the vault's layout stays the core's to define: its settings file, data folder,
 * database and entity folders are exactly what `plaintorch core init` makes. The one-shot runs beside a served core
 * without disturbing it: it binds no endpoint, and it neither reads nor writes the active vault.
 *
 * Needs a core executable (see {@link CoreProcess.locate}); the client flavour ships none, so it gets the reason and
 * the command to run instead. Resolves once the process has exited; never rejects.
 */
export function initializeVault(folder: string, profile: UserProfile): Promise<VaultInitialization> {
	const location = CoreProcess.locate()
	if (!location) {
		return Promise.resolve({
			initialized: false,
			reason: `No PLAINTORCH core is available to initialize it with. Run \`plaintorch core init --vault "${folder}"\`, then activate it again.`
		})
	}

	return new Promise(resolve => {
		let stderr = ""
		let settled = false
		const settle = (result: VaultInitialization) => {
			if (!settled) {
				settled = true
				clearTimeout(timer)
				resolve(result)
			}
		}

		const child = spawn(location.executable, ["init", "--vault", folder, "--profile", profile.root], {
			cwd: path.dirname(location.executable),
			stdio: ["ignore", "ignore", "pipe"],
			windowsHide: true,
			env: { ...process.env, DOTNET_NOLOGO: "1" }
		})
		const timer = setTimeout(() => {
			child.kill()
			settle({ initialized: false, reason: "The PLAINTORCH core did not finish initializing it in time." })
		}, initializationTimeoutMs)

		child.stderr?.setEncoding("utf8")
		child.stderr?.on("data", (chunk: string) => stderr += chunk)
		child.on("error", error => settle({ initialized: false, reason: `The PLAINTORCH core could not be started: ${error.message}` }))
		child.on("exit", code => {
			if (code === 0 && isVaultInitialized(folder)) {
				settle({ initialized: true })
				return
			}

			// A refusal says why on stderr (a database a newer core migrated); a crash prints its stack below the message.
			const reason = stderr.split(/\r?\n/).filter(line => line.trim() && !/^\s+at /.test(line)).join("\n").trim()
			settle({ initialized: false, reason: reason || `The PLAINTORCH core exited with code ${code ?? "?"}.` })
		})
	})
}
