import { createElement, icons } from 'lucide'
import type { IconHost } from '../../host'

/** `trash-2` → `Trash2`, `arrow-up-0-1` → `ArrowUp01`: the key lucide's icon map uses for a kebab-case name. */
const pascalOf = (name: string): string =>
	name.split('-').filter(Boolean).map(part => part[0]!.toUpperCase() + part.slice(1)).join('')

/** `Trash2` → `trash-2`, `Grid3x3` → `grid-3x3`: a kebab-case name that maps back to the same key. */
const kebabOf = (key: string): string =>
	key.replace(/([a-z])([A-Z0-9])/g, '$1-$2').replace(/([0-9])([A-Z])/g, '$1-$2').toLowerCase()

let names: readonly string[] | undefined

/**
 * The lucide glyphs, bundled from the `lucide` package. The map includes lucide's aliases, so a name Obsidian knew
 * under an older spelling still resolves where lucide kept it; a name lucide dropped (the brand icons) draws nothing.
 */
export const lucideIcons: IconHost = {
	lucide(name: string) {
		const node = icons[pascalOf(name.replace(/^lucide[:-]/, '')) as keyof typeof icons]
		return node ? createElement(node) : undefined
	},
	lucideNames() {
		return names ??= [...new Set(Object.keys(icons).map(kebabOf))].sort()
	},
}
