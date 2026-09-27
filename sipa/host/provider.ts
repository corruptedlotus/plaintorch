import type { PlatformHost, ToastKind } from './PlatformHost'

let current: PlatformHost | undefined

/**
 * Installs the application's {@link PlatformHost}. The application calls this once at bootstrap, before any SIPA
 * surface renders — the components reach for the host while they draw (an icon, an image), not only when acted on.
 * A later call replaces the host, which only a reloading application has reason to do.
 */
export function provideHost(platform: PlatformHost): void {
	current = platform
}

/** The installed {@link PlatformHost}. Throws when none was provided, so a missing bootstrap fails loudly. */
export function getHost(): PlatformHost {
	if (!current) {
		throw new Error('PLAINTORCH: no platform host — provideHost() must run before a SIPA surface renders.')
	}

	return current
}

/**
 * The installed {@link PlatformHost}, readable anywhere without holding on to it: every access is forwarded to the
 * host in place at that moment, and methods come back bound to it.
 */
export const host: PlatformHost = new Proxy({} as PlatformHost, {
	get(_, key) {
		const platform = getHost()
		const value = Reflect.get(platform, key, platform)
		return typeof value === 'function' ? value.bind(platform) : value
	},
	set: () => false,
})

/** Shows a short message through the host (see {@link PlatformHost.toast}). */
export function toast(message: string, kind: ToastKind = 'info'): void {
	getHost().toast(message, kind)
}
