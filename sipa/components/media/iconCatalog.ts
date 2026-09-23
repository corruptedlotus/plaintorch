import * as icons from '../../assets/icons'
import { getIconIds } from 'obsidian'

/** The `lucide:` scheme p7t-icon uses for a bundled lucide glyph (see {@link PleiadesIcon}). */
const lucideScheme = 'lucide:'

/** The `lucide-` prefix Obsidian registers its bundled lucide icon ids under (as returned by `getIconIds`). */
const lucideIdPrefix = 'lucide-'

/** Every bundled Pleiades glyph name, each usable directly as a `p7t-icon` key. */
export function pleiadesIconKeys(): string[] {
	return Object.keys(icons)
}

/**
 * Every Obsidian-bundled lucide icon, as a `p7t-icon` key. Obsidian registers them under `lucide-<name>` ids
 * (`getIconIds`), while `p7t-icon` expects the `lucide:<name>` form — so the prefix is rewritten here, the one
 * place the two conventions meet.
 */
export function lucideIconKeys(): string[] {
	return getIconIds()
		.filter(id => id.startsWith(lucideIdPrefix))
		.map(id => `${lucideScheme}${id.slice(lucideIdPrefix.length)}`)
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
