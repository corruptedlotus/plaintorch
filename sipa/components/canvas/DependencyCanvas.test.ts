import { describe, it, expect, beforeAll, afterEach, vi } from 'vitest'
// The components barrel first, in its own order: the canvas reaches its neighbours through it.
import '..'
import { DependencyEndpointKind, DependencyOrigin, type EndpointHit } from '@pleiades/sdk'
import { PlaintorchCoreClient, type PlaintorchCoreRequest, type PlaintorchCoreResponse, type PlaintorchCoreTransport } from '@pleiades/sdk/plaintorch'
import { provideHost, type PlatformHost } from '../../host'
import { provideCore } from '../data/coreProvider'
import type { ContextMenuAction, ContextMenuEntry, ContextMenuSpec } from '../design/ContextMenu'
import type { CanvasContextMode } from './graphContext'
import type { CanvasLayout, NodeBox, Point } from './graphLayout'
import type { CanvasGraph, CanvasNode } from './graphModel'
import { DependencyCanvas } from './DependencyCanvas'

// "Expand Connections" on a node of the global planning context: it pins the node's direct neighbours not on the canvas
// yet, intrinsic ones included, and is offered only while there is one. Neither onrush context offers it, since a node
// there is a membership of the sprint.

const { Directive, Objective, Checkpoint } = DependencyEndpointKind

const end = (kind: DependencyEndpointKind, id: string) => ({ kind, id })

/** The edge listing the scripted core serves: o1 → d1 → c1, the child d2 feeding d1, and o9 → o1 two hops away. */
const edges = [
	[end(Objective, 'o1'), end(Directive, 'd1'), DependencyOrigin.Explicit],
	[end(Directive, 'd1'), end(Checkpoint, 'c1'), DependencyOrigin.Explicit],
	[end(Directive, 'd2'), end(Directive, 'd1'), DependencyOrigin.Intrinsic],
	[end(Objective, 'o9'), end(Objective, 'o1'), DependencyOrigin.Explicit]
].map(([source, target, origin], index) => {
	const from = source as ReturnType<typeof end>
	const to = target as ReturnType<typeof end>
	return {
		'@type': 'Dependency', id: origin === DependencyOrigin.Intrinsic ? 0 : index + 1, origin,
		sourceKind: from.kind, sourceId: from.id, sourceRecurrenceId: null,
		targetKind: to.kind, targetId: to.id, targetRecurrenceId: null,
		satisfied: false, gatesNextTransition: false
	}
})

beforeAll(() => {
	provideHost({
		toast: () => undefined,
		media: { resourceUrl: () => undefined, isResourceUrl: () => false },
		icons: { lucide: () => undefined, lucideNames: () => [] }
	} as unknown as PlatformHost)
	provideCore(scriptedCore())
})

afterEach(() => {
	vi.restoreAllMocks()
	document.body.replaceChildren()
})

/** A core serving {@link edges}, once `gate` lets the edge listing through. */
function scriptedCore(gate: Promise<void> = Promise.resolve()) {
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
			const body = request.method === 'GET' && request.path === '/api/dependencies' ? edges : undefined
			if (body) {
				await gate
			}

			return { ok: body !== undefined, status: body !== undefined ? 200 : 404, text: async () => JSON.stringify(body ?? null), header: () => undefined }
		}
	}
	return new PlaintorchCoreClient({ transports: [transport] })
}

/** The canvas's private surface this suite reads and drives. */
interface CanvasInternals {
	readonly graph: CanvasGraph
	readonly dependencies: { readonly value: readonly unknown[] | undefined }
	readonly pan: Point
	readonly scale: number
	overrides: ReadonlyMap<string, Point>
	nodeMenuSpecFor(node: CanvasNode): ContextMenuSpec
	layoutFor(graph: CanvasGraph): CanvasLayout
	boxesFor(layout: CanvasLayout): Map<string, NodeBox>
}

async function mountGlobal(pinned: EndpointHit[]) {
	const canvas = document.createElement('p7t-dependency-canvas') as DependencyCanvas
	canvas.mode = 'global'
	canvas.pinned = pinned
	document.body.append(canvas)
	const internals = canvas as unknown as CanvasInternals
	await vi.waitFor(() => expect(internals.dependencies.value).toHaveLength(edges.length))
	return { canvas, internals }
}

const labels = (entries: readonly ContextMenuEntry[]) => entries.map(entry => 'label' in entry ? entry.label : 'separator' in entry ? '—' : entry.note)

const expandRow = (internals: CanvasInternals, key: string) => {
	const node = internals.graph.nodes.find(candidate => candidate.key === key)!
	return internals.nodeMenuSpecFor(node).entries
		.find((entry): entry is ContextMenuAction => 'label' in entry && entry.label === 'Expand Connections')
}

const pin = (kind: DependencyEndpointKind, id: string, title = id): EndpointHit => ({ kind, id, title })

/** Gives the canvas's viewport a size, which happy-dom lays nothing out to give it, so the canvas can frame. */
function stubViewport(width: number, height: number) {
	vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
		return this.classList.contains('viewport') ? new DOMRect(0, 0, width, height) : new DOMRect()
	})
}

/** Where a node shows across the viewport, from the canvas's own pan, zoom and placement. */
function screenSpan(internals: CanvasInternals, key: string) {
	const box = internals.boxesFor(internals.layoutFor(internals.graph)).get(key)!
	const left = internals.pan.x + box.x * internals.scale
	return { left, right: left + box.width * internals.scale, top: internals.pan.y + box.y * internals.scale }
}

describe('DependencyCanvas Expand Connections', () => {
	it('pins the direct neighbours both ways, intrinsic included, then stops offering itself', async () => {
		const { canvas, internals } = await mountGlobal([pin(Directive, 'd1', 'The directive'), pin(Checkpoint, 'c1')])

		const row = expandRow(internals, 'Directive:d1')
		expect(row).toBeDefined()
		row!.run({ x: 0, y: 0 } as never)

		expect(canvas.pinned.map(hit => `${DependencyEndpointKind[hit.kind]}:${hit.id}`))
			.toEqual(['Directive:d1', 'Checkpoint:c1', 'Objective:o1', 'Directive:d2'])
		expect(expandRow(internals, 'Directive:d1')).toBeUndefined()
		// o1 still has a neighbour off the canvas — o9, two hops from d1, which the expansion did not reach.
		expect(expandRow(internals, 'Objective:o1')).toBeDefined()
	})

	it('is offered on every kind of node with a neighbour off the canvas, and on none without', async () => {
		const { internals } = await mountGlobal([pin(Checkpoint, 'c1'), pin(Objective, 'o9')])
		expect(expandRow(internals, 'Checkpoint:c1')).toBeDefined()
		expect(expandRow(internals, 'Objective:o9')).toBeDefined()

		const lone = await mountGlobal([pin(Objective, 'o1'), pin(Objective, 'o9'), pin(Directive, 'd1')])
		expect(expandRow(lone.internals, 'Objective:o9')).toBeUndefined()
	})

	it('places the neighbours beside a node the reader placed: prerequisites before it, dependants after', async () => {
		const { canvas, internals } = await mountGlobal([pin(Directive, 'd1')])
		internals.overrides = new Map([['Directive:d1', { x: 1000, y: 500 }]])

		expandRow(internals, 'Directive:d1')!.run({ x: 0, y: 0 } as never)

		expect(canvas.pinned).toHaveLength(4)
		const placed = internals.overrides
		expect(placed.get('Objective:o1')!.x).toBeLessThan(1000)
		expect(placed.get('Directive:d2')!.x).toBeLessThan(1000)
		expect(placed.get('Checkpoint:c1')!.x).toBeGreaterThan(1000)
		// Two prerequisites share the column before it, one above the other rather than on top of each other.
		expect(Math.abs(placed.get('Objective:o1')!.y - placed.get('Directive:d2')!.y)).toBeGreaterThanOrEqual(76)
	})

	it('leaves the layout to place them beside a node it placed, and keeps that node where it was on screen', async () => {
		stubViewport(1600, 900)
		const { canvas, internals } = await mountGlobal([pin(Directive, 'd1')])
		await vi.waitFor(() => expect((internals as CanvasInternals & { readonly framed: boolean }).framed).toBe(true))
		const before = screenSpan(internals, 'Directive:d1')
		expandRow(internals, 'Directive:d1')!.run({ x: 0, y: 0 } as never)

		expect(canvas.pinned).toHaveLength(4)
		expect(internals.overrides.size).toBe(0)
		// The layout made room for a rank before it, so the node moved on the surface, and the view followed it.
		expect(internals.boxesFor(internals.layoutFor(internals.graph)).get('Directive:d1')!.x).toBeGreaterThan(48)
		expect(screenSpan(internals, 'Directive:d1')).toEqual(before)
		for (const key of ['Objective:o1', 'Directive:d2', 'Checkpoint:c1']) {
			expect(screenSpan(internals, key).left).toBeGreaterThanOrEqual(48)
			expect(screenSpan(internals, key).right).toBeLessThanOrEqual(1600 - 48)
		}
	})

	it('moves the view as little as brings new neighbours into sight when they would fall outside it', async () => {
		stubViewport(700, 900)
		const { internals } = await mountGlobal([pin(Objective, 'o9'), pin(Objective, 'o1')])
		await vi.waitFor(() => expect((internals as CanvasInternals & { readonly framed: boolean }).framed).toBe(true))
		const before = screenSpan(internals, 'Objective:o1')
		expandRow(internals, 'Objective:o1')!.run({ x: 0, y: 0 } as never)

		// d1 lands a rank after o1, past the right edge; the view shifts left just enough to show it.
		const expanded = screenSpan(internals, 'Objective:o1')
		const added = screenSpan(internals, 'Directive:d1')
		expect(expanded.left).toBeLessThan(before.left)
		expect(expanded.left).toBeGreaterThanOrEqual(48)
		expect(added.right).toBeCloseTo(700 - 48, 5)
	})

	it('frames a pinned set only once its edges have answered, so the ranks they make are what fits the view', async () => {
		stubViewport(1600, 900)
		let release!: () => void
		provideCore(scriptedCore(new Promise<void>(resolve => release = resolve)))
		try {
			const canvas = document.createElement('p7t-dependency-canvas') as DependencyCanvas
			canvas.mode = 'global'
			canvas.pinned = [pin(Objective, 'o1'), pin(Directive, 'd1'), pin(Checkpoint, 'c1')]
			document.body.append(canvas)
			const internals = canvas as unknown as CanvasInternals & { readonly framed: boolean }
			await canvas.updateComplete
			await new Promise(resolve => setTimeout(resolve, 20))
			expect(internals.framed).toBe(false)

			release()
			await vi.waitFor(() => expect(internals.framed).toBe(true))
			// Three ranks, o1 → d1 → c1, all inside the viewport.
			const boxes = [...internals.boxesFor(internals.layoutFor(internals.graph)).values()]
			expect(new Set(boxes.map(box => box.x)).size).toBe(3)
			for (const box of boxes) {
				const left = internals.pan.x + box.x * internals.scale
				expect(left).toBeGreaterThanOrEqual(0)
				expect(left + box.width * internals.scale).toBeLessThanOrEqual(1600)
			}
		}
		finally {
			provideCore(scriptedCore())
		}
	})

	it.each(['onrush-active', 'onrush-planning'] as CanvasContextMode[])('is not offered in the %s context, member or ghost', async mode => {
		const { canvas, internals } = await mountGlobal([pin(Directive, 'd1')])
		canvas.mode = mode
		const member: CanvasNode = { key: 'Directive:d1', ref: end(Directive, 'd1'), entity: { id: 'd1', title: 'The directive' } }
		for (const node of [member, { ...member, ghostly: true }, { key: 'Checkpoint:c1', ref: end(Checkpoint, 'c1'), entity: { id: 'c1', title: 'c1' } }]) {
			expect(labels(internals.nodeMenuSpecFor(node).entries)).not.toContain('Expand Connections')
		}
	})
})
