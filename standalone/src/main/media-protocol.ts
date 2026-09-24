import path from "node:path"
import { pathToFileURL } from "node:url"
import { net, protocol } from "electron"
import { mediaScheme } from "../shared/contracts"

/**
 * Registers the vault media scheme as a privileged, standard one. Must run before the app is ready, which is when
 * Electron fixes the schemes a renderer may load from.
 */
export function registerMediaScheme(): void {
	protocol.registerSchemesAsPrivileged([
		{ scheme: mediaScheme, privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true } }
	])
}

/**
 * Serves the vault's files to the renderer — the custom icons and banner images the components show — as
 * `plaintorch-media://vault/<vault-relative path>`, from whichever vault the core serves at the time of the request.
 * The sandboxed renderer has no file access of its own, and a path that resolves outside the vault is refused.
 */
export function handleMediaScheme(servedVault: () => string | undefined): void {
	protocol.handle(mediaScheme, request => {
		const vault = servedVault()
		const url = new URL(request.url)
		if (!vault || url.host !== "vault") {
			return new Response(null, { status: 404 })
		}

		const root = path.resolve(vault)
		const target = path.resolve(root, decodeURIComponent(url.pathname).replace(/^\/+/, ""))
		if (target !== root && !target.startsWith(root + path.sep)) {
			return new Response(null, { status: 403 })
		}

		return net.fetch(pathToFileURL(target).href)
	})
}
