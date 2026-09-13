import { identify, type LorePage } from '@pleiades/sdk'
import type { GridRow } from './entityTree'

/**
 * Flattens the lore pages into the rows the grid renders, mirroring {@link buildGridRows} for the backlog.
 *
 * Lore is a strict Era → Chapter → Act → Phase hierarchy of one entity, so the shape comes entirely from each page's
 * `parentId`. Siblings are ordered by narrative index, and a page whose parent is missing is surfaced as a root
 * rather than dropped. Whether a row is {@link GridRow.active} is read from the page's `isActive` flag, which the API
 * stamps from the one active definition (the core's LoreIndex) — the grid no longer re-derives its own spine.
 */
export function buildLoreRows(pages: readonly LorePage[], toggled: ReadonlySet<string>, expandedByDefault = false): GridRow[] {
	const known = new Set(pages.map(page => page.id))
	const roots: LorePage[] = []
	const byParent = new Map<string, LorePage[]>()

	for (const page of pages) {
		const parent = page.parentId
		if (parent && known.has(parent)) {
			group(byParent, parent, page)
		} else {
			roots.push(page)
		}
	}

	roots.sort(compareNarrative)
	for (const siblings of byParent.values()) {
		siblings.sort(compareNarrative)
	}

	const rows: GridRow[] = []
	const emit = (page: LorePage, guides: string[]) => {
		const children = byParent.get(page.id) ?? []
		const expandable = children.length > 0
		const key = loreRowKey(page)
		// Lore is a small narrative hierarchy meant to be read as a whole, so it renders expanded by default: the
		// toggled set then tracks which rows the user has collapsed rather than which they have opened.
		const isExpanded = expandable && (expandedByDefault ? !toggled.has(key) : toggled.has(key))

		rows.push({
			key,
			entity: page,
			kind: 'lore',
			depth: guides.length,
			guides,
			expandable,
			expanded: isExpanded,
			active: page.isActive ?? false
		})

		if (!isExpanded) {
			return
		}

		for (const child of children) {
			emit(child, [...guides, 'lore'])
		}
	}

	for (const era of roots) {
		emit(era, [])
	}

	return rows
}

/** Identifies a lore row, and the page behind it, in the entity store. */
export function loreRowKey(page: LorePage): string {
	return identify(page) ?? `LorePage:${page.id}`
}

/** Orders lore siblings by narrative index (era, chapter, act, phase), falling back to the terminal identifier. */
function compareNarrative(left: LorePage, right: LorePage): number {
	return compareIndex(left.era, right.era)
		|| compareIndex(left.chapter, right.chapter)
		|| compareIndex(left.act, right.act)
		|| compareIndex(left.phase, right.phase)
		|| terminalIdentifier(left).localeCompare(terminalIdentifier(right))
}

/** Compares two nullable indices, ordering a present number before an absent one — the same order the core uses. */
function compareIndex(left: number | undefined, right: number | undefined): number {
	if (left != null && right != null) {
		return left - right
	}

	if (left != null) {
		return -1
	}

	if (right != null) {
		return 1
	}

	return 0
}

function terminalIdentifier(page: LorePage): string {
	return (page.id ?? '').split('/').pop() ?? ''
}

function group(map: Map<string, LorePage[]>, key: string, value: LorePage): void {
	const existing = map.get(key)
	if (existing) {
		existing.push(value)
	} else {
		map.set(key, [value])
	}
}
