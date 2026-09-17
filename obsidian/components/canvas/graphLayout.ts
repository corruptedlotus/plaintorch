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

/**
 * Reads saved node positions back into overrides, keyed by node key (PEP102).
 *
 * Forgiving by design: anything malformed, or a stored value that is not a finite pair, is skipped rather
 * than thrown on — a layout is a hint, and a corrupt one should cost a re-placement, not a broken canvas.
 */
export function parsePositions(json: string | undefined): Map<string, Point> {
	const positions = new Map<string, Point>()
	if (!json) {
		return positions
	}

	let parsed: unknown
	try {
		parsed = JSON.parse(json)
	}
	catch {
		return positions
	}

	if (!parsed || typeof parsed !== 'object') {
		return positions
	}

	for (const [key, value] of Object.entries(parsed as Record<string, unknown>)) {
		const point = value as { x?: unknown, y?: unknown }
		if (typeof point?.x === 'number' && Number.isFinite(point.x) && typeof point?.y === 'number' && Number.isFinite(point.y)) {
			positions.set(key, { x: point.x, y: point.y })
		}
	}

	return positions
}

/** Serialises overrides for the column, or undefined when there is nothing to remember (PEP102). */
export function serializePositions(positions: ReadonlyMap<string, Point>): string | undefined {
	if (positions.size === 0) {
		return undefined
	}

	const record: Record<string, Point> = {}
	for (const [key, point] of positions) {
		record[key] = point
	}

	return JSON.stringify(record)
}

/** A side of a node box an edge may leave from or arrive at. */
export type Side = 'left' | 'right' | 'top' | 'bottom'

/** The outward unit normal of each side — the direction an edge travels as it leaves that side. */
const normals: Record<Side, Point> = {
	left: { x: -1, y: 0 },
	right: { x: 1, y: 0 },
	top: { x: 0, y: -1 },
	bottom: { x: 0, y: 1 }
}

/** The middle of one side of a box. */
export function sideAnchor(box: NodeBox, side: Side): Point {
	switch (side) {
		case 'left': return { x: box.x, y: box.y + box.height / 2 }
		case 'right': return { x: box.x + box.width, y: box.y + box.height / 2 }
		case 'top': return { x: box.x + box.width / 2, y: box.y }
		case 'bottom': return { x: box.x + box.width / 2, y: box.y + box.height }
	}
}

function centre(box: NodeBox): Point {
	return { x: box.x + box.width / 2, y: box.y + box.height / 2 }
}

/**
 * The side of a box most accessible from a point — the one facing it.
 *
 * Judged in the box's own proportions: the offset to the point is measured in half-widths and half-heights,
 * so a wide node keeps its left or right side for anything not clearly above or below it, and hands over to
 * its top or bottom only once the point sits more over the node than beside it. That is what lets the
 * ordinary left-to-right layout keep its horizontal reading while a node dragged above its neighbour gets a
 * vertical edge instead of a loop. Ties fall to the horizontal, the layout's flow.
 */
export function facingSide(box: NodeBox, towards: Point): Side {
	const from = centre(box)
	const dx = (towards.x - from.x) / Math.max(box.width / 2, 1)
	const dy = (towards.y - from.y) / Math.max(box.height / 2, 1)
	if (Math.abs(dx) >= Math.abs(dy)) {
		return dx >= 0 ? 'right' : 'left'
	}

	return dy >= 0 ? 'bottom' : 'top'
}

/** An edge's ends, each on the side of its box that faces the other end. */
export interface EdgeEnds {
	readonly from: Point
	readonly fromSide: Side
	readonly to: Point
	readonly toSide: Side
}

/**
 * Chooses where an edge leaves its source and arrives at its target: each end sits on the side of its own
 * box that faces the *other box's centre*, so a dependant to the right is entered from the left, one below
 * from the top, and one dragged behind its prerequisite is reached by leaving the source's left rather than
 * looping around from its right.
 */
export function edgeEnds(source: NodeBox, target: NodeBox): EdgeEnds {
	const fromSide = facingSide(source, centre(target))
	const toSide = facingSide(target, centre(source))
	return { from: sideAnchor(source, fromSide), fromSide, to: sideAnchor(target, toSide), toSide }
}

/**
 * Draws the curve between two ends as a cubic whose tangents run out of each side.
 *
 * Each control point lies along its own side's outward normal, so the edge leaves the source perpendicular
 * to the side it exits and arrives at the target perpendicular to the side it enters — a horizontal-tangent
 * curve between left and right sides, a vertical one between top and bottom, and a clean quarter-turn between
 * a horizontal side and a vertical one. The reach grows with the distance so a long edge stays gently bowed.
 */
export function edgeCurve(from: Point, fromSide: Side, to: Point, toSide: Side): string {
	const reach = Math.max(40, Math.hypot(to.x - from.x, to.y - from.y) * 0.4)
	const out = normals[fromSide]
	const into = normals[toSide]
	const c1 = { x: from.x + out.x * reach, y: from.y + out.y * reach }
	const c2 = { x: to.x + into.x * reach, y: to.y + into.y * reach }
	return `M ${from.x} ${from.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${to.x} ${to.y}`
}

/**
 * The point halfway along the curve {@link edgeCurve} draws — where a badge rides.
 *
 * Evaluated at the parameter midpoint of the cubic, `(P0 + 3·P1 + 3·P2 + P3) / 8`, rather than the mean of
 * the ends: with the tangents no longer level with their ends the two differ, and the badge would float off
 * the line on a bent edge.
 */
export function edgeMidpoint(from: Point, fromSide: Side, to: Point, toSide: Side): Point {
	const reach = Math.max(40, Math.hypot(to.x - from.x, to.y - from.y) * 0.4)
	const out = normals[fromSide]
	const into = normals[toSide]
	const c1 = { x: from.x + out.x * reach, y: from.y + out.y * reach }
	const c2 = { x: to.x + into.x * reach, y: to.y + into.y * reach }
	return {
		x: (from.x + 3 * c1.x + 3 * c2.x + to.x) / 8,
		y: (from.y + 3 * c1.y + 3 * c2.y + to.y) / 8
	}
}

/**
 * The curve of an edge still being drawn: it leaves the source from the side facing the pointer and arrives
 * at the pointer head-on, from whichever direction the pointer approaches.
 */
export function linkCurve(source: NodeBox, pointer: Point): string {
	const fromSide = facingSide(source, pointer)
	const from = sideAnchor(source, fromSide)
	const toSide: Side = fromSide === 'left' ? 'right' : fromSide === 'right' ? 'left' : fromSide === 'top' ? 'bottom' : 'top'
	return edgeCurve(from, fromSide, pointer, toSide)
}
