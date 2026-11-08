import { identify, type LorePage } from '@pleiades/sdk'
import type { GridNode } from './entityTree'

/**
 * Builds the tree of lore pages the lore grid renders, mirroring {@link buildEntityTree} for the backlog.
 *
 * Lore is a strict Era → Chapter → Act → Phase hierarchy of one entity, so the shape comes entirely from each page's
 * `parentId`. Siblings are ordered by narrative index, and a page whose parent is missing is surfaced as a root
 * rather than dropped. Whether a node is {@link GridNode.active} is read from the page's `isActive` flag, which the API
 * stamps from the one active definition (the core's LoreIndex) — the grid no longer re-derives its own spine.
 */
export function buildLoreTree(pages: readonly LorePage[]): GridNode[] {
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

	// A page nested under its own descendant would otherwise recurse forever; page paths make that unlikely, but the
	// vault is hand-editable.
	const visiting = new Set<string>()
	const node = (page: LorePage, guides: string[]): GridNode | undefined => {
		if (visiting.has(page.id)) {
			return undefined
		}

		visiting.add(page.id)
		const children = (byParent.get(page.id) ?? [])
			.map(child => node(child, [...guides, 'lore']))
			.filter(child => child !== undefined)
		visiting.delete(page.id)

		return {
			id: loreRowKey(page),
			entity: page,
			kind: 'lore',
			guides,
			children,
			active: page.isActive ?? false
		}
	}

	return roots
		.map(era => node(era, []))
		.filter(era => era !== undefined)
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
