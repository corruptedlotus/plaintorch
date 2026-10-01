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
 * One node of a grid's tree, as a grid variant builds it.
 *
 * Deliberately generic so every grid variant — the backlog's directives/incentives, the lore hierarchy — produces
 * the same shape and reuses one layout: `entity` is the node's payload of whatever kind, `kind` and `guides` are
 * plain strings the row draws its icon and indent lining from, and `active` marks a node a variant considers current.
 *
 * The grid's data-grid controller flattens the tree through {@link children} and keeps it expanded or collapsed per
 * node; it tells nodes apart by {@link id}, so that name is load-bearing.
 */
export interface GridNode {
	/**
	 * Stable across rebuilds, so the same entity keeps its row element — and its expansion — as the tree is rebuilt.
	 * The data-grid controller keys its expansion state by an `id` field, hence the name.
	 */
	readonly id: string
	readonly entity: { readonly id: string }
	readonly kind: string
	/**
	 * One entry per indent lane, outermost first, naming the lining that lane draws. Its length is the
	 * row's depth, and its last entry is the group this row itself belongs to.
	 */
	readonly guides: readonly string[]
	/** The nodes nested under this one, in the order they are shown. Absent or empty for a leaf. */
	readonly children?: readonly GridNode[]
	/**
	 * Whether a node with children can be collapsed at all. A heading that only groups rows — the world heading —
	 * keeps them in sight, and so is never expandable. Defaults to `true`.
	 */
	readonly collapsible?: boolean
	/** Whether the node is part of its grid's "active" set (the lore spine); grids without one leave it unset. */
	readonly active?: boolean
	/**
	 * Whether the node heads a group that currently holds nothing. Such a row stands by out of sight: it is there
	 * to be a drop target, and shows itself only while something that could land in its group is being dragged.
	 */
	readonly vacant?: boolean
}

/**
 * One rendered line of a grid: a {@link GridNode} as its row element sees it, once the grid has placed it.
 *
 * Everything a row needs to draw itself travels here, so a row element never reaches for the grid: whether it can be
 * opened and is, and which of the grid's columns it draws cells for, in the order the user arranged them.
 */
export interface GridRow extends GridNode {
	/** The node's {@link GridNode.id}; identifies the row in the grid's expansion requests. */
	readonly key: string
	readonly depth: number
	readonly expandable: boolean
	readonly expanded: boolean
	/** The keys of the grid's visible columns, in display order. The row draws one cell per key. */
	readonly columns: readonly string[]
}

/**
 * What a grid shows of its tree. An entity that passes is shown inside its hierarchy: every ancestor it sits under is
 * shown with it, whether or not that ancestor passes itself.
 */
export interface GridCriteria {
	/** Whether an entity passes. */
	matches(entity: GridEntity): boolean
	/**
	 * Whether the reader is searching — has typed or chosen something — rather than only standing defaults being in
	 * force. While a search is on, the grid opens its rows so that what matched is in sight.
	 */
	readonly active: boolean
}

/** The flat listings a tree is composed from. */
export interface EntityTreeSource {
	readonly directives?: readonly Directive[]
	readonly objectives?: readonly Objective[]
	readonly fates?: readonly Fate[]
	readonly decrees?: readonly Decree[]
}

/**
 * The stellar backlog the listings hold: the stellar directives, and the incentives that are not a lunar directive's.
 *
 * Lunar directives and what they own live on the Moonlight page. Their incentives are dropped along with them rather
 * than left behind, which would surface them here as world incentives — which they are not.
 */
export function stellarBacklogSource(source: EntityTreeSource): EntityTreeSource {
	const lunarIds = new Set(source.directives?.filter(directive => directive.isLunar).map(directive => directive.id))
	const outsideMoonlight = <T extends { directiveId?: string }>(incentives: readonly T[] | undefined) =>
		incentives?.filter(incentive => !incentive.directiveId || !lunarIds.has(incentive.directiveId))

	return {
		directives: source.directives?.filter(directive => !directive.isLunar),
		objectives: outsideMoonlight(source.objectives),
		fates: outsideMoonlight(source.fates),
		decrees: outsideMoonlight(source.decrees)
	}
}

/** Identifies a row, and the entity behind it, in the entity store. */
export function gridRowKey(entity: GridEntity): string {
	return identify(entity) ?? `?:${entity.id}`
}

/**
 * The key of the column holding each row's lead — the indent lanes, the kind icon and the title. Every grid variant
 * has it, first.
 */
export const gridTitleColumn = 'title'

/** The key of the world heading, the group every incentive without a directive sits in. */
export const worldHeadingKey = 'world-incentives'

/**
 * Builds the tree of directives and their incentives the backlog grid renders.
 *
 * Only the shape is decided here: which node sits under which, and in what order. Flattening, indentation depth and
 * expansion belong to the grid, which walks the tree through each node's {@link GridNode.children}; every row still
 * ends up a direct child of one grid, so the columns line up however deep a row sits. The lining is drawn inside each
 * row from its own {@link GridNode.guides}.
 *
 * Only the entities that pass `include` are shown, each inside its hierarchy: a directive that does not pass itself is
 * still drawn while anything beneath it does, so a result never loses its ancestors (see {@link GridCriteria}).
 */
export function buildEntityTree(source: EntityTreeSource, include: (entity: GridEntity) => boolean = () => true): GridNode[] {
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

	// A directive reparented under its own descendant would otherwise recurse forever. The vault is
	// hand-editable, so this is a reachable state rather than a theoretical one.
	const visiting = new Set<string>()

	const incentiveNode = (entity: GridEntity, kind: 'incentive' | 'world', guides: readonly string[]): GridNode => ({
		id: gridRowKey(entity),
		entity,
		kind,
		guides
	})

	const directiveNode = (directive: Directive, guides: readonly GridGuide[]): GridNode | undefined => {
		if (visiting.has(directive.id)) {
			return undefined
		}

		visiting.add(directive.id)
		const children = [
			...[...subdirectives.get(directive.id) ?? []].sort(byTitle)
				.map(child => directiveNode(child, [...guides, 'directive'])),
			...[...owned.get(directive.id) ?? []].filter(include).sort(byTitle)
				.map(incentive => incentiveNode(incentive, 'incentive', [...guides, 'incentive']))
		].filter(node => node !== undefined)
		visiting.delete(directive.id)

		// Shown when it passes, or as the ancestor of something beneath it that does.
		if (children.length === 0 && !include(directive)) {
			return undefined
		}

		return {
			id: gridRowKey(directive),
			entity: directive,
			kind: 'directive',
			guides,
			children
		}
	}

	const roots = [...rootDirectives].sort(byTitle)
		.map(directive => directiveNode(directive, []))
		.filter(node => node !== undefined)

	// Incentives with no directive are first-level citizens rather than being hidden: they sit under the world
	// heading, which never collapses. The heading is also where a drag lands to take an entity out from under its
	// directive, so it is emitted even with nothing beneath it — vacant, and unseen until a drag needs it. An empty
	// grid has nothing to drag, and keeps its "nothing here" notice.
	const worldIncentives = rootIncentives.filter(include)
	if (worldIncentives.length > 0 || roots.length > 0) {
		roots.push({
			id: worldHeadingKey,
			entity: null!,
			kind: 'root',
			guides: [],
			collapsible: false,
			vacant: worldIncentives.length === 0,
			children: worldIncentives.sort(byTitle).map(incentive => incentiveNode(incentive, 'world', ['world']))
		})
	}

	return roots
}

/**
 * Determines whether following a directive's parents ever reaches a directive that has none.
 *
 * A vault is hand-editable, so a parent chain that loops back on itself is a reachable state rather than
 * a theoretical one, and every directive in such a loop would otherwise be invisible. Shared by every surface that
 * draws a directive tree, so each surfaces the same directives at the top.
 */
export function descendsFromARoot(
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
