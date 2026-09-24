import * as icons from '../../assets/icons'
import { host } from '../../host'

/** The `lucide:` scheme p7t-icon uses for a lucide glyph the host draws (see {@link PleiadesIcon}). */
const lucideScheme = 'lucide:'

/** Every bundled Pleiades glyph name, each usable directly as a `p7t-icon` key. */
export function pleiadesIconKeys(): string[] {
	return Object.keys(icons)
}

/** Every lucide icon the host can draw, as a `p7t-icon` key (`lucide:<name>`). */
export function lucideIconKeys(): string[] {
	return host.icons.lucideNames().map(name => `${lucideScheme}${name}`)
}

/** The full catalog: Pleiades glyphs first, then every bundled lucide icon. */
export function allIconKeys(): string[] {
	return [...pleiadesIconKeys(), ...lucideIconKeys()]
}

/**
 * Searches the icon catalog. A `lucide:`-prefixed query restricts the search to lucide icons and matches on the
 * name after the scheme; any other query matches Pleiades glyphs and lucide icons alike, on their whole key (so a
 * lucide hit keeps its `lucide:` scheme in the result). An empty query lists the whole catalog.
 */
export function searchIconKeys(query: string, limit = 50): string[] {
	const trimmed = query.trim().toLowerCase()
	const lucideOnly = trimmed.startsWith(lucideScheme)
	const needle = lucideOnly ? trimmed.slice(lucideScheme.length) : trimmed
	const pool = lucideOnly ? lucideIconKeys() : allIconKeys()
	const matches = needle ? pool.filter(key => key.toLowerCase().includes(needle)) : pool
	return matches.slice(0, limit)
}
