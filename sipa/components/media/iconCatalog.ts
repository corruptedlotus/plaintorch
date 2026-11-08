import * as icons from '../../assets/icons'
import { host } from '../../host'
import { fuzzyFilter } from '../editing/fuzzy'

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
 * Searches the icon catalog by {@link fuzzyFilter}, best match first, on each icon's name — its key without the
 * `lucide:` scheme, though a lucide hit keeps the scheme in the result. A `lucide:`-prefixed query restricts the
 * search to lucide icons; any other query matches Pleiades glyphs and lucide icons alike. An empty query lists the
 * whole catalog.
 */
export function searchIconKeys(query: string, limit = 50): string[] {
	const trimmed = query.trim()
	const lucideOnly = trimmed.toLowerCase().startsWith(lucideScheme)
	const needle = lucideOnly ? trimmed.slice(lucideScheme.length) : trimmed
	const pool = lucideOnly ? lucideIconKeys() : allIconKeys()
	return fuzzyFilter(needle, pool, iconName).slice(0, limit)
}

/** An icon key's name: the key itself for a Pleiades glyph, the part after the scheme for a lucide icon. */
function iconName(key: string): string {
	return key.startsWith(lucideScheme) ? key.slice(lucideScheme.length) : key
}
