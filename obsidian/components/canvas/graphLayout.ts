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
 * Where a point lies in the space around an origin box, in the terms the side selection reasons in.
 *
 * The box's four edges, extended, cut the plane into four *facings* — the bands straight across each side —
 * and four *quadrants* between them. A point in a facing has one obvious side. A point in a quadrant is
 * placed by its angle from the box's nearest corner: 0 along the horizontal edge's extension, 90° along the
 * vertical one's, so the quadrant's bisector (`Qm`) is 45°, and each facing's direct choice extends
 * {@link directExpansion} degrees into the quadrant (`Q2` off the horizontal, `Q1` off the vertical, in the
 * sketch this follows). The same partition surrounds every side: which quadrant the point is in only decides
 * which corner the angle is taken from and which pair of sides is in play.
 */

/** The quadrant bisector: below it the origin leaves horizontally, above it vertically. */
const quadrantBisector = 45
/** How far past its facing a side's *direct* target choice still holds, before the edge swings off-axis. */
const directExpansion = 10
interface Placement {
	/** The origin's side facing the point horizontally, and the target's side facing back — or none, if level. */
	readonly horizontal?: { readonly origin: Side, readonly target: Side }
	/** The same vertically. */
	readonly vertical?: { readonly origin: Side, readonly target: Side }
	/** Degrees from the horizontal edge's extension, when the point is in a quadrant (both sides set). */
	readonly angle: number
}

function place(origin: NodeBox, point: Point): Placement {
	const beyondRight = point.x - (origin.x + origin.width)
	const beyondLeft = origin.x - point.x
	const beyondBottom = point.y - (origin.y + origin.height)
	const beyondTop = origin.y - point.y

	const horizontal = beyondRight > 0 ? { origin: 'right' as const, target: 'left' as const, by: beyondRight }
		: beyondLeft > 0 ? { origin: 'left' as const, target: 'right' as const, by: beyondLeft }
			: undefined
	const vertical = beyondBottom > 0 ? { origin: 'bottom' as const, target: 'top' as const, by: beyondBottom }
		: beyondTop > 0 ? { origin: 'top' as const, target: 'bottom' as const, by: beyondTop }
			: undefined

	const angle = horizontal && vertical ? Math.atan2(vertical.by, horizontal.by) * 180 / Math.PI : 0
	return { horizontal, vertical, angle }
}

/**
 * The point a target is aimed at: its centre, pulled a quarter of its width and height towards the origin's
 * centre. The nearer face is what an edge reaches for, and judging by the whole centre would let a wide or
 * tall target tip the choice from further away than its near face really sits.
 */
function faceMidpoint(target: NodeBox, origin: NodeBox): Point {
	const from = centre(origin)
	const to = centre(target)
	return {
		x: to.x - Math.sign(to.x - from.x) * target.width / 4,
		y: to.y - Math.sign(to.y - from.y) * target.height / 4
	}
}

/** An edge's ends, each on the side of its box that faces the other end. */
export interface EdgeEnds {
	readonly from: Point
	readonly fromSide: Side
	readonly to: Point
	readonly toSide: Side
}

/**
 * Chooses the sides an edge leaves and arrives by, reasoning from the origin.
 *
 * The target is aimed at by its {@link faceMidpoint}. In a facing of the origin the choice is direct: the
 * origin leaves by that side and the target is entered by the side facing back. In a quadrant the angle from
 * the origin's corner decides both, in the pattern the sketch lays down:
 *
 * - **Origin side**: the horizontal side up to the bisector `Qm` (45°), the vertical one beyond — each side's
 *   facing expands by half a quadrant on either flank.
 * - **Target side**: the facing side's direct choice holds up to `Q2` (10° off the horizontal); between `Q2`
 *   and `Qm` the edge swings to the target's off-axis side (its vertical face); past `Qm` the same rule
 *   mirrors around the vertical facing — direct (the vertical face) from `Qy` down to `Q1` (10° off the
 *   vertical), off-axis (the horizontal face) between `Q1` and `Qm`.
 *
 * The rule is stated for one quadrant but holds in all four: the partition is symmetric about the origin,
 * and the placement names the sides in play for whichever quadrant the target falls in.
 *
 * So an edge that leaves horizontally enters horizontally near the axis and vertically nearer the diagonal,
 * and one that leaves vertically does the converse, which is what keeps a near-diagonal edge from hooking
 * around a corner it could simply meet.
 */
export function edgeEnds(source: NodeBox, target: NodeBox): EdgeEnds {
	const [fromSide, toSide] = chooseSides(source, faceMidpoint(target, source))
	return { from: sideAnchor(source, fromSide), fromSide, to: sideAnchor(target, toSide), toSide }
}

function chooseSides(origin: NodeBox, aim: Point): [Side, Side] {
	const { horizontal, vertical, angle } = place(origin, aim)
	if (horizontal && !vertical) {
		return [horizontal.origin, horizontal.target]
	}

	if (vertical && !horizontal) {
		return [vertical.origin, vertical.target]
	}

	if (!horizontal || !vertical) {
		// The aim lies inside the origin — overlapping boxes. Leave by whichever side the centres separate on.
		const from = centre(origin)
		const dx = aim.x - from.x
		const dy = aim.y - from.y
		return Math.abs(dx) >= Math.abs(dy)
			? [dx >= 0 ? 'right' : 'left', dx >= 0 ? 'left' : 'right']
			: [dy >= 0 ? 'bottom' : 'top', dy >= 0 ? 'top' : 'bottom']
	}

	if (angle < quadrantBisector) {
		return [horizontal.origin, angle < directExpansion ? horizontal.target : vertical.target]
	}

	return [vertical.origin, angle >= 90 - directExpansion ? vertical.target : horizontal.target]
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
	// The pointer is a point, not a box: it is its own face midpoint, and the sides are chosen as for a target.
	const [fromSide, toSide] = chooseSides(source, pointer)
	return edgeCurve(sideAnchor(source, fromSide), fromSide, pointer, toSide)
}
