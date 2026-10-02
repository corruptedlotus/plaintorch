import { DecreeStatus, LunarDirectiveStatus, type Decree, type Directive } from '@pleiades/sdk'
import { descendsFromARoot, type EntityTreeSource, type GridEntity } from '../grid/entityTree'

/** One lunar directive with everything beneath it, the shape the Moonlight page draws. */
export interface MoonlightNode {
	readonly directive: Directive
	/** Its child lunar directives, by title. */
	readonly subdirectives: readonly MoonlightNode[]
	/** The objectives, fates and decrees it owns directly, by title. */
	readonly incentives: readonly GridEntity[]
}

/**
 * Nests the lunar directives, and the incentives each owns, into one tree per top-level lunar directive.
 *
 * Stellar directives are ignored, along with anything they own; a lunar hierarchy stays lunar (PEP100). As in the
 * backlog grid, a directive whose parent is missing, or whose parent chain loops back on itself, is surfaced at the top
 * rather than stranded where nothing reaches it: the vault is hand-editable, so both are reachable states.
 */
export function buildMoonlightTree(source: EntityTreeSource): MoonlightNode[] {
	const lunar = (source.directives ?? []).filter(directive => directive.isLunar)
	const byId = new Map(lunar.map(directive => [directive.id, directive]))
	const known = new Set(byId.keys())
	const children = new Map<string, Directive[]>()
	const owned = new Map<string, GridEntity[]>()
	const roots: Directive[] = []

	for (const directive of lunar) {
		const parent = directive.parentDirectiveId
		if (parent && known.has(parent) && descendsFromARoot(directive, byId, known)) {
			group(children, parent, directive)
		} else {
			roots.push(directive)
		}
	}

	const incentives: GridEntity[] = [...source.objectives ?? [], ...source.fates ?? [], ...source.decrees ?? []]
	for (const incentive of incentives) {
		const owner = (incentive as { directiveId?: string }).directiveId
		if (owner && known.has(owner)) {
			group(owned, owner, incentive)
		}
	}

	// A directive reparented under its own descendant would otherwise recurse forever.
	const visiting = new Set<string>()
	const nodeOf = (directive: Directive): MoonlightNode => {
		visiting.add(directive.id)
		const node: MoonlightNode = {
			directive,
			subdirectives: [...children.get(directive.id) ?? []]
				.filter(child => !visiting.has(child.id))
				.sort(byTitle)
				.map(nodeOf),
			incentives: [...owned.get(directive.id) ?? []].sort(byTitle)
		}
		visiting.delete(directive.id)
		return node
	}

	return roots.sort(byTitle).map(nodeOf)
}

/** A decree taking part in Lunar Reflection, beside the directive that owns it. */
export interface LunarReflectionEntry {
	readonly decree: Decree
	readonly directive: Directive
}

/**
 * The decrees that take part in Lunar Reflection, by title: the active ones marked to reflect whose lunar hierarchy is
 * active.
 *
 * The same test the core applies before it generates a cycle's reflectives (`IsLunarHierarchyActiveAsync`, PEP104): a
 * decree's `reflect` flag only counts inside a lunar hierarchy, and only while every lunar directive on its lineage —
 * its own directive and each ancestor — is Active. An On Hold or Stale one pauses its whole subtree; stellar directives'
 * states are not consulted. The walk remembers where it has been, since a hand-edited vault can loop.
 */
export function buildLunarReflection(source: Pick<EntityTreeSource, 'directives' | 'decrees'>): LunarReflectionEntry[] {
	const byId = new Map((source.directives ?? []).map(directive => [directive.id, directive]))
	const inActiveLunarHierarchy = (directive: Directive) => {
		const seen = new Set<string>()
		let foundLunar = false
		for (let current: Directive | undefined = directive; current && !seen.has(current.id); current = byId.get(current.parentDirectiveId ?? '')) {
			if (current.isLunar) {
				if (current.status !== LunarDirectiveStatus.Active) {
					return false
				}

				foundLunar = true
			}

			seen.add(current.id)
		}

		return foundLunar
	}

	const entries: LunarReflectionEntry[] = []
	for (const decree of source.decrees ?? []) {
		const directive = byId.get(decree.directiveId ?? '')
		if (decree.reflect && decree.status === DecreeStatus.Active && directive && inActiveLunarHierarchy(directive)) {
			entries.push({ decree, directive })
		}
	}

	return entries.sort((a, b) => byTitle(a.decree, b.decree))
}

function group<T>(map: Map<string, T[]>, key: string, value: T): void {
	const existing = map.get(key)
	if (existing) {
		existing.push(value)
	} else {
		map.set(key, [value])
	}
}

function byTitle(a: { title: string }, b: { title: string }): number {
	return a.title.localeCompare(b.title)
}
