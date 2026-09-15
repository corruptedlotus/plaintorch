import { homedir, userInfo } from "node:os"
import path from "node:path"
import { isDevelopmentBuild } from "./flavor"

/**
 * The per-user PLAINTORCH profile this shell and its core share, mirroring `PlaintorchUserLayout` in the core: the
 * root under `~/.pleiades`, the config file, the log folder, and the transport endpoint clients connect to.
 */
export interface UserProfile {
	/** The profile root directory. */
	root: string
	/** Whether this is the dev sub-profile rather than the real per-user one. */
	isDevelopment: boolean
	/** The shared `config.json`. */
	configurationPath: string
	/** Where a daemon or spawned core writes its daily logs. */
	logsPath: string
	/** The Windows named pipe name (without the `\\.\pipe\` prefix). */
	pipeName: string
	/** The AF_UNIX socket path used everywhere but Windows. */
	socketPath: string
	/** What a Node HTTP client passes as `socketPath` on this platform. */
	transportPath: string
	/** The endpoint as the core displays it. */
	endpointDisplay: string
}

/**
 * Resolves the profile for this build. An explicit root (from `--profile <dir>` on the shell's own command line)
 * wins; otherwise a development build uses the dev sub-profile and a production build the real one.
 */
export function resolveUserProfile(explicitRoot?: string): UserProfile {
	const root = explicitRoot
		? path.resolve(explicitRoot)
		: path.join(homedir(), ".pleiades", isDevelopmentBuild ? "plaintorch-dev" : "plaintorch")
	// Mirrors the core: `{profile folder}.{user}` so profiles and users never collide on the machine-global pipe namespace.
	const pipeName = `${path.basename(root)}.${userInfo().username}`
	const socketPath = path.join(root, "plaintorch.sock")
	const isWindows = process.platform === "win32"
	const transportPath = isWindows ? `\\\\.\\pipe\\${pipeName}` : socketPath
	return {
		root,
		isDevelopment: !explicitRoot && isDevelopmentBuild,
		configurationPath: path.join(root, "config.json"),
		logsPath: path.join(root, "logs"),
		pipeName,
		socketPath,
		transportPath,
		endpointDisplay: transportPath
	}
}
