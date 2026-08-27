import { identify, type Decree, type Directive, type Fate, type Objective } from '@pleiades/sdk'

/** An entity the grid can render as a row. */
export type GridEntity = Directive | Objective | Fate | Decree

/** What a row is, structurally. Only directives nest. */
export type GridRowKind = 'directive' | 'incentive'

/**
 * The lining drawn in one indent lane, naming what kind of group that level holds.
 *
 * A directive's children are emitted as two groups — its subdirectives, then its incentives — and the two
 * are told apart by their lining rather than by any marker on the rows themselves.
 */
export type GridGuide = GridRowKind

/**
 * One rendered line of a grid.
 *
 * Deliberately generic so every grid variant — the backlog's directives/incentives, the lore hierarchy — produces
 * the same row shape and reuses one layout: `entity` is the row's payload of whatever kind, `kind` and `guides` are
 * plain strings the row draws its icon and indent lining from, and `active` marks a row a variant considers current.
 */
export interface GridRow {
	/** Stable across rebuilds, so Lit keeps the same element for the same entity. */
	readonly key: string
	readonly entity: { readonly id: string }
	readonly kind: string
	readonly depth: number
	/**
	 * One entry per indent lane, outermost first, naming the lining that lane draws. Its length is the
	 * row's depth, and its last entry is the group this row itself belongs to.
	 */
	readonly guides: readonly string[]
	readonly expandable: boolean
	readonly expanded: boolean
	/** Whether the row is part of its grid's "active" set (the lore spine); grids without one leave it unset. */
	readonly active?: boolean
}

/** The flat listings a tree is composed from. */
export interface EntityTreeSource {
	readonly directives?: readonly Directive[]
	readonly objectives?: readonly Objective[]
	readonly fates?: readonly Fate[]
	readonly decrees?: readonly Decree[]
}

/** Identifies a row, and the entity behind it, in the entity store. */
export function gridRowKey(entity: GridEntity): string {
	return identify(entity) ?? `?:${entity.id}`
}

/**
 * Flattens directives and their incentives into the rows the grid renders.
 *
 * Flat rather than nested on purpose: every row is then a direct child of one grid, so the columns line up
 * across the whole list no matter how deep a row sits. Indentation and lining are drawn inside each row
 * from its own {@link GridRow.guides}, which is what lets nesting go arbitrarily deep without the layout
 * having to know about it.
 */
export function buildGridRows(source: EntityTreeSource, expanded: ReadonlySet<string>): GridRow[] {
	const directives = source.directives ?? []
	const incentives: GridEntity[] = [
		...(source.objectives ?? []),
		...(source.fates ?? []),
		...(source.decrees ?? [])
	]

	const byId = new Map(directives.map(directive => [directive.id, directive]))
	const known = new Set(byId.keys())
	const subdirectives = new Map<string, Directive[]>()
	const owned = new Map<string, GridEntity[]>()
	const rootDirectives: Directive[] = []
	const rootIncentives: GridEntity[] = []

	for (const directive of directives) {
		const parent = directive.parentDirectiveId
		// A parent outside this listing, or one that only leads back around to this directive, would
		// otherwise strand the whole subtree where nothing could reach it. The grid's job is to show what
		// exists, so anything unreachable is surfaced at the top rather than quietly dropped.
		if (parent && known.has(parent) && descendsFromARoot(directive, byId, known)) {
			group(subdirectives, parent, directive)
		} else {
			rootDirectives.push(directive)
		}
	}

	for (const incentive of incentives) {
		const parent = (incentive as { directiveId?: string }).directiveId
		if (parent && known.has(parent)) {
			group(owned, parent, incentive)
		} else {
			rootIncentives.push(incentive)
		}
	}

	const rows: GridRow[] = []
	// A directive reparented under its own descendant would otherwise recurse forever. The vault is
	// hand-editable, so this is a reachable state rather than a theoretical one.
	const visiting = new Set<string>()

	const emitIncentive = (entity: GridEntity, guides: readonly GridGuide[]) => {
		rows.push({
			key: gridRowKey(entity),
			entity,
			kind: 'incentive',
			depth: guides.length,
			guides,
			expandable: false,
			expanded: false
		})
	}

	const emitDirective = (directive: Directive, guides: readonly GridGuide[]) => {
		if (visiting.has(directive.id)) {
			return
		}

		const children = subdirectives.get(directive.id) ?? []
		const ownIncentives = owned.get(directive.id) ?? []
		const key = gridRowKey(directive)
		const expandable = children.length > 0 || ownIncentives.length > 0
		const isExpanded = expandable && expanded.has(key)

		rows.push({
			key,
			entity: directive,
			kind: 'directive',
			depth: guides.length,
			guides,
			expandable,
			expanded: isExpanded
		})

		if (!isExpanded) {
			return
		}

		visiting.add(directive.id)
		for (const child of [...children].sort(byTitle)) {
			emitDirective(child, [...guides, 'directive'])
		}

		for (const incentive of [...ownIncentives].sort(byTitle)) {
			emitIncentive(incentive, [...guides, 'incentive'])
		}

		visiting.delete(directive.id)
	}

	for (const directive of [...rootDirectives].sort(byTitle)) {
		emitDirective(directive, [])
	}

	// Incentives with no directive are first-level citizens rather than being hidden or grouped under a
	// synthetic parent.
	for (const incentive of [...rootIncentives].sort(byTitle)) {
		emitIncentive(incentive, [])
	}

	return rows
}

/**
 * Determines whether following a directive's parents ever reaches a directive that has none.
 *
 * A vault is hand-editable, so a parent chain that loops back on itself is a reachable state rather than
 * a theoretical one, and every directive in such a loop would otherwise be invisible.
 */
function descendsFromARoot(
	directive: Directive,
	byId: ReadonlyMap<string, Directive>,
	known: ReadonlySet<string>
): boolean {
	const seen = new Set<string>([directive.id])
	let parent = directive.parentDirectiveId
	while (parent && known.has(parent)) {
		if (seen.has(parent)) {
			return false
		}

		seen.add(parent)
		parent = byId.get(parent)?.parentDirectiveId
	}

	return true
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
