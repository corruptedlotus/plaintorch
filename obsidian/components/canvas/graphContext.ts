import { DependencyEndpointKind, type Checkpoint, type Dependency, type Objective, type OnrushSprint } from '@pleiades/sdk'
import { endpointKey, resolveEdges, sourceRef, targetRef, type CanvasEntity, type CanvasGraph, type CanvasNode } from './graphModel'

/**
 * Which slice of the backlog the canvas is showing.
 *
 * The mode is not only a filter: it decides what adding or removing a node *means*. In an onrush context a
 * node is a membership of that sprint, so putting one on the canvas puts it in the sprint — which is why the
 * modes are named here, next to the functions that resolve them, rather than left to the surface.
 */
export type CanvasContextMode = 'onrush-active' | 'onrush-planning'

/** What each mode is called where a person has to choose one. */
export const contextModeLabels: Record<CanvasContextMode, string> = {
	'onrush-active': 'Active Onrush',
	'onrush-planning': 'Planning Onrush'
}

/** Resolves an out-of-context endpoint to the entity behind it, for drawing a ghostly blocker. */
export type EndpointResolver = (kind: DependencyEndpointKind, id: string) => CanvasEntity | undefined

/**
 * Resolves the graph of an onrush sprint.
 *
 * Every objective and checkpoint the sprint holds becomes a node, including the ones no edge touches: in this
 * context the canvas shows the sprint, and a member with no dependencies is still part of it. The milestone is
 * one of the checkpoints, flagged.
 *
 * Beyond the members, the graph pulls in the prerequisites *outside* the sprint that hold a member back — an
 * unmet dependency whose blocking end is not itself in the sprint. Those are drawn ghostly: shown so the block
 * is visible, but not part of the sprint, so not removable, and gone once the block resolves (they simply stop
 * being pulled in). An eventive endpoint names its owner under a different kind, so an edge to one occurrence
 * of a recurring entity is not an edge to the entity and is left out.
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
	const milestoneId = sprint?.milestoneCheckpointId
	const checkpoints = (sprint?.checkpoints ?? []).filter(isEntity).map(checkpoint => checkpointNode(checkpoint, checkpoint.id === milestoneId))

	const members = [...objectives, ...checkpoints]
	const memberKeys = new Set(members.map(node => node.key))
	const ghosts = ghostlyBlockers(dependencies, memberKeys, resolve)

	const nodes = [...members, ...ghosts]
	const keys = new Set(nodes.map(node => node.key))
	return { nodes, edges: resolveEdges(dependencies, keys) }
}

/**
 * The out-of-context prerequisites blocking a member (PEP102).
 *
 * One per unsatisfied edge whose dependant is a member and whose blocking source is not, resolved to whatever
 * the source names. A source that cannot be resolved still appears, labelled by its id, so a block is never
 * silently hidden. Deduplicated: one ghostly node however many members a single blocker holds back.
 */
function ghostlyBlockers(
	dependencies: readonly Dependency[],
	memberKeys: ReadonlySet<string>,
	resolve: EndpointResolver
): CanvasNode[] {
	const ghosts = new Map<string, CanvasNode>()
	for (const dependency of dependencies) {
		if (dependency.satisfied) {
			continue
		}

		const target = endpointKey(targetRef(dependency))
		const source = sourceRef(dependency)
		const sourceId = endpointKey(source)
		if (!memberKeys.has(target) || memberKeys.has(sourceId) || ghosts.has(sourceId)) {
			continue
		}

		const entity = resolve(source.kind, source.id) ?? { id: source.id, title: source.id }
		ghosts.set(sourceId, { key: sourceId, ref: source, entity, ghostly: true })
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

/** Addresses a checkpoint as a dependency endpoint, flagging the sprint's milestone. */
export function checkpointNode(checkpoint: Checkpoint, milestone: boolean): CanvasNode {
	const ref = { kind: DependencyEndpointKind.Checkpoint, id: checkpoint.id }
	return { key: endpointKey(ref), ref, entity: checkpoint, milestone }
}
