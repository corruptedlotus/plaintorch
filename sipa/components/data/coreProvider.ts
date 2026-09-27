import type { PlaintorchCoreClient } from '@pleiades/sdk/plaintorch'

let current: PlaintorchCoreClient | undefined

/**
 * Installs the core client the UI talks through. The application calls this once at bootstrap, before the first
 * SIPA element is constructed: several elements take their repository from the client as they are built, and keep
 * it. The Obsidian plugin provides the node client (named pipe / unix socket); the standalone shell provides a
 * client whose transport crosses its preload bridge.
 *
 * Provide it once. Replacing the client later would leave mounted views on the old one's repositories — reconnection
 * is the transport's and the change feed's business, not a reason to swap clients.
 */
export function provideCore(client: PlaintorchCoreClient): void {
	current = client
}

/** The installed core client. Throws when none was provided, so a missing bootstrap fails loudly. */
export function getCore(): PlaintorchCoreClient {
	if (!current) {
		throw new Error('PLAINTORCH: no core client — provideCore() must run before a SIPA surface touches the core.')
	}

	return current
}

/**
 * The core client. `core.repos` gives cached, observable reads — use those for anything a surface displays and must
 * keep current. The domain SDKs on `core` stay the way to run a one-shot query (a picker's search) or an imperative
 * vault command.
 *
 * Every access is forwarded to the client installed with {@link provideCore} at that moment, and methods come back
 * bound to it, so the binding can be imported anywhere without holding on to a client.
 */
export const core: PlaintorchCoreClient = new Proxy({} as PlaintorchCoreClient, {
	get(_, key) {
		const client = getCore()
		const value = Reflect.get(client, key, client)
		return typeof value === 'function' ? value.bind(client) : value
	},
	set: () => false,
})
