import { DependencyEndpointKind, type Dependency, type Objective, type OnrushSprint } from '@pleiades/sdk'
import { endpointKey, resolveEdges, type CanvasGraph, type CanvasNode } from './graphModel'

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

/**
 * Resolves the graph of an onrush sprint.
 *
 * Every objective the sprint holds becomes a node, including the ones no edge touches: in this context the
 * canvas shows the sprint, and an objective with no dependencies is still part of it. Only objectives appear,
 * because only objectives are what a sprint holds.
 *
 * An eventive endpoint carries its owner's id under a different kind, so an edge drawn to one occurrence of a
 * recurring objective is not an edge to the objective and is left out — deliberately, until a context exists
 * that can draw occurrences.
 */
export function onrushContext(sprint: OnrushSprint | undefined, dependencies: readonly Dependency[]): CanvasGraph {
	const nodes = (sprint?.objectives ?? []).map(objectiveNode)
	const keys = new Set(nodes.map(node => node.key))
	return { nodes, edges: resolveEdges(dependencies, keys) }
}

/** Addresses an objective as a dependency endpoint. */
export function objectiveNode(objective: Objective): CanvasNode {
	const ref = { kind: DependencyEndpointKind.Objective, id: objective.id }
	return { key: endpointKey(ref), ref, entity: objective }
}
