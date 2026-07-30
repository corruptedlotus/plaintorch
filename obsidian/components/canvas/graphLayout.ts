import { graphlib, layout } from '@dagrejs/dagre'
import type { CanvasGraph } from './graphModel'

export interface Point {
	readonly x: number
	readonly y: number
}

/** Where a node sits, as a top-left corner and a size — what absolute positioning needs. */
export interface NodeBox extends Point {
	readonly width: number
	readonly height: number
}

/** The placed graph. Keys are the node keys the graph was built with. */
export interface CanvasLayout {
	readonly nodes: ReadonlyMap<string, NodeBox>
	readonly width: number
	readonly height: number
}

export interface LayoutOptions {
	readonly nodeWidth?: number
	readonly nodeHeight?: number
	/** Distance between ranks — horizontal, since the flow runs left to right. */
	readonly rankSeparation?: number
	/** Distance between nodes sharing a rank. */
	readonly nodeSeparation?: number
	readonly margin?: number
}

const defaults = {
	nodeWidth: 240,
	nodeHeight: 76,
	rankSeparation: 110,
	nodeSeparation: 32,
	margin: 48
}

export const emptyLayout: CanvasLayout = {
	nodes: new Map(),
	width: 0,
	height: 0
}

/**
 * Places a graph, left to right, prerequisites before dependants.
 *
 * The only file that knows dagre exists. Everything downstream reads {@link CanvasLayout}, so replacing the
 * engine — or hand-rolling one — is a change to this file alone.
 *
 * Only the placement is taken from dagre, not its edge routing: a node the reader has dragged invalidates
 * every polyline that was routed around where it used to be, and a curve drawn between two boxes stays
 * honest wherever they end up. See {@link edgeCurve}.
 */
export function layoutGraph(graph: CanvasGraph, options: LayoutOptions = {}): CanvasLayout {
	if (graph.nodes.length === 0) {
		return emptyLayout
	}

	const settings = { ...defaults, ...options }
	// Multigraph so each edge can carry its own key: two objectives can be joined by more than one dependency
	// (a different trigger or constraint), and without names the second would overwrite the first in the
	// layout model — and naming an edge at all is rejected unless the graph is a multigraph.
	const model = new graphlib.Graph({ directed: true, multigraph: true })
	model.setGraph({
		rankdir: 'LR',
		ranksep: settings.rankSeparation,
		nodesep: settings.nodeSeparation,
		marginx: settings.margin,
		marginy: settings.margin
	})
	// dagre asks for an edge label factory even when no edge carries one.
	model.setDefaultEdgeLabel(() => ({}))

	for (const node of graph.nodes) {
		model.setNode(node.key, { width: settings.nodeWidth, height: settings.nodeHeight })
	}

	for (const edge of graph.edges) {
		model.setEdge(edge.source, edge.target, {}, edge.key)
	}

	layout(model)

	const nodes = new Map<string, NodeBox>()
	let width = 0
	let height = 0
	for (const node of graph.nodes) {
		const placed = model.node(node.key)
		if (!placed) {
			continue
		}

		// dagre reports a centre; a positioned element wants its corner.
		const box: NodeBox = {
			x: placed.x - placed.width / 2,
			y: placed.y - placed.height / 2,
			width: placed.width,
			height: placed.height
		}
		nodes.set(node.key, box)
		width = Math.max(width, box.x + box.width)
		height = Math.max(height, box.y + box.height)
	}

	return {
		nodes,
		width: width + settings.margin,
		height: height + settings.margin
	}
}

/** Where an edge leaves a node: the middle of its trailing edge, since the flow runs left to right. */
export function exitPoint(box: NodeBox): Point {
	return { x: box.x + box.width, y: box.y + box.height / 2 }
}

/** Where an edge arrives at a node. */
export function entryPoint(box: NodeBox): Point {
	return { x: box.x, y: box.y + box.height / 2 }
}

/**
 * Draws the curve between two points as a horizontal-tangent cubic.
 *
 * Both control points sit level with their own end, so every edge leaves the source travelling right and
 * arrives at the target travelling right — the reading direction of the layout, kept even when a dependant
 * has been dragged behind its prerequisite.
 */
export function edgeCurve(from: Point, to: Point): string {
	const reach = Math.max(40, Math.abs(to.x - from.x) * 0.5)
	return `M ${from.x} ${from.y} C ${from.x + reach} ${from.y}, ${to.x - reach} ${to.y}, ${to.x} ${to.y}`
}
