import { homedir, userInfo } from "node:os"
import path from "node:path"
import { createLoopbackBaseUrl } from "./internal/transport"
import { NodeSocketPlaintorchCoreTransport } from "./nodeTransport"
import { PlaintorchCoreClient, type PlaintorchCoreClientOptions } from "./coreClient"
export interface NodePlaintorchCoreClientOptions extends Omit<PlaintorchCoreClientOptions, "transports"> {
	socketPath?: string
}

// Compile-time flag baked into the bundle by the bundler (see the Obsidian plugin's esbuild `define`). A dev/watch
// build substitutes `true` here, so the default client targets the persistent dev sub-profile socket a manual `serve`
// binds (`~/.pleiades/plaintorch-dev`); a production build substitutes `false`. The `typeof` guard keeps the SDK safe
// when it is consumed without the define at all (the identifier is simply absent from the output) — it then falls back
// to the real per-user profile (`~/.pleiades/plaintorch`). This is a build-time substitution, not a runtime env read,
// so it survives into the shipped bundle regardless of the process environment it later runs in.
declare const __PLAINTORCH_DEV_PROFILE__: boolean
const useDevProfile = typeof __PLAINTORCH_DEV_PROFILE__ !== "undefined" && __PLAINTORCH_DEV_PROFILE__
const defaultProfileDirectory = useDevProfile ? "plaintorch-dev" : "plaintorch"
// On Windows the core binds a named pipe, because Node resolves a socket path to a named pipe there and cannot reach a
// .NET AF_UNIX socket; every other platform uses the AF_UNIX socket under the profile directory. The pipe name mirrors
// the host's: `{profileDir}.{user}` — per-profile and per-user (Windows pipe names match case-insensitively).
const defaultSocketPath = process.platform === "win32"
	? `\\\\.\\pipe\\${defaultProfileDirectory}.${userInfo().username}`
	: path.join(homedir(), ".pleiades", defaultProfileDirectory, "plaintorch.sock")
export class NodePlaintorchCoreClient extends PlaintorchCoreClient {
	public constructor(options: NodePlaintorchCoreClientOptions = {}) {
		// Node clients talk to the core exclusively over its per-user unix domain socket. `serve` always binds the
		// socket (the loopback HTTP endpoint is opt-in and, when present, is shared across instances), so no
		// fetch/loopback fallback is wired up — a fallback could silently cross-talk to a different instance (e.g. the
		// real daemon on the shared loopback port) whenever the intended socket is unavailable. `baseUrl` is still
		// resolved because the base client uses it to build static asset URLs (`icon()`), not for API requests.
		const baseUrl = options.baseUrl ?? createLoopbackBaseUrl(options.host ?? "127.0.0.1", options.loopbackPort ?? 43118)
		const socketTransport = new NodeSocketPlaintorchCoreTransport(options.socketPath ?? defaultSocketPath)
		super({
			...options,
			baseUrl,
			transports: [socketTransport]
		})
	}
}

export { NodeSocketPlaintorchCoreTransport }
