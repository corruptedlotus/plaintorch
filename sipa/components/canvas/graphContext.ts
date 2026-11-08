import { DependencyEndpointKind, type Checkpoint, type Dependency, type Directive, type EndpointHit, type EndpointRef, type Objective, type OnrushSprint } from '@pleiades/sdk'
import { endpointKey, resolveEdges, sourceRef, targetRef, unresolvedPrerequisites, type CanvasEntity, type CanvasGraph, type CanvasNode } from './graphModel'

/**
 * Which slice of the backlog the canvas is showing.
 *
 * The mode is not only a filter: it decides what adding or removing a node *means*. In an onrush context a
 * node is a membership of that sprint, so putting one on the canvas puts it in the sprint. In the global
 * context a node is only *shown*: adding and removing hide and reveal, and never touch the edges themselves —
 * which is why the modes are named here, next to the functions that resolve them, rather than left to the
 * surface.
 */
export type CanvasContextMode = 'onrush-active' | 'onrush-planning' | 'global'

/** What each mode is called where a person has to choose one. */
export const contextModeLabels: Record<CanvasContextMode, string> = {
	'onrush-active': 'Active Onrush',
	'onrush-planning': 'Planning Onrush',
	'global': 'Global Planning'
}

/** Resolves an out-of-context endpoint to the entity behind it, for drawing a ghostly blocker. */
export type EndpointResolver = (kind: DependencyEndpointKind, id: string) => CanvasEntity | undefined

/**
 * Resolves the graph of an onrush sprint.
 *
 * Every objective, stellar directive and checkpoint the sprint holds becomes a node, including the ones no edge
 * touches: in this context the canvas shows the sprint, and a member with no dependencies is still part of it. The
 * milestone is one of the checkpoints, flagged. A directive member brings its intrinsic edges with it (PEP102 patch
 * 9): each of its children that is also a member is drawn feeding its finish, dashed.
 *
 * Beyond the members, the graph pulls in the prerequisites *outside* the sprint that hold a member back — and
 * not only the immediate ones: the whole unmet chain, each blocker's own blocker on down, so a member held
 * back at two removes shows what is really holding it. Those are drawn ghostly: shown so the block is visible,
 * but not part of the sprint, so not removable, and gone once the block resolves (they simply stop being
 * pulled in). An eventive endpoint names its owner under a different kind, so an edge to one occurrence of a
 * recurring entity is not an edge to the entity and is left out.
 */
export function onrushContext(
	sprint: OnrushSprint | undefined,
	dependencies: readonly Dependency[],
	resolve: EndpointResolver = () => undefined
): CanvasGraph {
	// Guarded rather than mapped straight over: a listing is only ever as good as what reached it, and the
	// same defensiveness the entity tree applies to a hand-edited vault applies to a collection that
	// travelled here as part of some other entity's payload.
	const objectives = (sprint?.objectives ?? []).filter(isEntity).map(objectiveNode)
	const directives = (sprint?.directives ?? []).filter(isEntity).map(directiveNode)
	const milestoneId = sprint?.milestoneCheckpointId
	const checkpoints = (sprint?.checkpoints ?? []).filter(isEntity).map(checkpoint => checkpointNode(checkpoint, checkpoint.id === milestoneId))

	const members = [...objectives, ...directives, ...checkpoints]
	const memberKeys = new Set(members.map(node => node.key))
	const ghosts = ghostlyBlockers(dependencies, memberKeys, resolve)

	const nodes = [...members, ...ghosts]
	const keys = new Set(nodes.map(node => node.key))
	return { nodes, edges: resolveEdges(dependencies, keys) }
}

/**
 * The out-of-context prerequisites blocking a member, transitively (PEP102).
 *
 * The unmet chain reachable from the members — every blocker of a member, every blocker of one of those, and
 * on down (see {@link unresolvedPrerequisites}) — minus the members themselves, which are already nodes. Each
 * is resolved to whatever it names; one that cannot be resolved still appears, labelled by its id, so a block
 * is never silently hidden. Deduplicated: one ghostly node however many members, or other ghosts, a single
 * blocker holds back.
 */
function ghostlyBlockers(
	dependencies: readonly Dependency[],
	memberKeys: ReadonlySet<string>,
	resolve: EndpointResolver
): CanvasNode[] {
	const ghosts = new Map<string, CanvasNode>()
	for (const refs of unresolvedPrerequisites(dependencies, memberKeys).values()) {
		for (const source of refs) {
			const sourceId = endpointKey(source)
			if (memberKeys.has(sourceId) || ghosts.has(sourceId)) {
				continue
			}

			const entity = resolve(source.kind, source.id) ?? { id: source.id, title: source.id }
			ghosts.set(sourceId, { key: sourceId, ref: source, entity, ghostly: true })
		}
	}

	return [...ghosts.values()]
}

function isEntity<T extends { id?: string }>(value: T | undefined | null): value is T {
	return !!value && typeof value.id === 'string' && value.id.length > 0
}

/** Addresses an objective as a dependency endpoint. */
export function objectiveNode(objective: Objective): CanvasNode {
	const ref = { kind: DependencyEndpointKind.Objective, id: objective.id }
	return { key: endpointKey(ref), ref, entity: objective }
}

/** Addresses a stellar directive as a dependency endpoint (PEP102 patch 10). */
export function directiveNode(directive: Directive): CanvasNode {
	const ref = { kind: DependencyEndpointKind.Directive, id: directive.id }
	return { key: endpointKey(ref), ref, entity: directive }
}

/** Addresses a checkpoint as a dependency endpoint, flagging the sprint's milestone. */
export function checkpointNode(checkpoint: Checkpoint, milestone: boolean): CanvasNode {
	const ref = { kind: DependencyEndpointKind.Checkpoint, id: checkpoint.id }
	return { key: endpointKey(ref), ref, entity: checkpoint, milestone }
}

/**
 * Resolves the graph of a global planning context (PEP102 — the freeform phase).
 *
 * The nodes are exactly what the reader has pinned, in whatever mix of kinds; the edges are every dependency
 * running between two of them. Unlike an onrush this holds no membership — a node is here because it was
 * *shown*, so removing it only hides it and the edge it carried survives, ready to reappear when it is pinned
 * again. Each pin carries its own title, so a context restored from a file draws before anything is fetched;
 * a live entity is preferred when the resolver has one, for its current fields.
 */
export function globalContext(
	pinned: readonly EndpointHit[],
	dependencies: readonly Dependency[],
	resolve: EndpointResolver = () => undefined
): CanvasGraph {
	const nodes: CanvasNode[] = []
	const seen = new Set<string>()
	for (const hit of pinned) {
		const ref = { kind: hit.kind, id: hit.id }
		const key = endpointKey(ref)
		if (seen.has(key)) {
			continue
		}

		seen.add(key)
		nodes.push({ key, ref, entity: resolve(hit.kind, hit.id) ?? { id: hit.id, title: hit.title } })
	}

	return { nodes, edges: resolveEdges(dependencies, seen) }
}

/** The key a pin is shown under in a {@link globalContext}: its kind and id, never an occurrence slot. */
export function pinKey(hit: Pick<EndpointHit, 'kind' | 'id'>): string {
	return endpointKey({ kind: hit.kind, id: hit.id })
}

/**
 * The endpoints one edge away from an endpoint, as the pins a global context needs to show those edges: its direct
 * neighbours in both directions — what it depends on and what depends on it — intrinsic edges included, so a
 * directive's open children and the parent it feeds count alike. Only direct neighbours: the chain is not walked.
 *
 * An edge touches the endpoint when one of its ends has the endpoint's key exactly, so an edge on one occurrence of an
 * eventive touches that occurrence, not its owner. The far end becomes a pin of its kind and id, as a global context
 * holds it: an eventive neighbour is pinned as an eventive under its owner's id, its slot dropped, the same as "Add
 * with dependencies" always pinned one. Each pin is named by `resolve`, falling back to its id, so the pin — and a
 * `.p7tpx` it is saved to — carries a title rather than a PUCK wherever the entity is loaded.
 *
 * Deduplicated by pin key, in the order the edges list them, and never the endpoint itself or anything in `present`
 * (the {@link pinKey}s already shown).
 */
export function neighbourPins(
	dependencies: readonly Dependency[],
	endpoint: EndpointRef,
	present: ReadonlySet<string> = new Set(),
	resolve: EndpointResolver = () => undefined
): EndpointHit[] {
	const key = endpointKey(endpoint)
	const seen = new Set([pinKey(endpoint)])
	const pins: EndpointHit[] = []
	for (const dependency of dependencies) {
		const source = sourceRef(dependency)
		const target = targetRef(dependency)
		const far = endpointKey(source) === key ? target
			: endpointKey(target) === key ? source
				: undefined
		if (!far) {
			continue
		}

		const farKey = pinKey(far)
		if (seen.has(farKey) || present.has(farKey)) {
			continue
		}

		seen.add(farKey)
		pins.push({ kind: far.kind, id: far.id, title: resolve(far.kind, far.id)?.title ?? far.id })
	}

	return pins
}

/**
 * The keys of the shown endpoints that have a direct neighbour not yet shown: those {@link neighbourPins} would add to,
 * given the same `present` pin keys. One pass over the edges, so a surface can decide for every node at once whether
 * an expansion would bring anything in.
 */
export function expandableKeys(dependencies: readonly Dependency[], present: ReadonlySet<string>): Set<string> {
	const expandable = new Set<string>()
	for (const dependency of dependencies) {
		const source = sourceRef(dependency)
		const target = targetRef(dependency)
		const sourceKey = endpointKey(source)
		const targetKey = endpointKey(target)
		if (present.has(sourceKey) && !present.has(pinKey(target))) {
			expandable.add(sourceKey)
		}

		if (present.has(targetKey) && !present.has(pinKey(source))) {
			expandable.add(targetKey)
		}
	}

	return expandable
}
