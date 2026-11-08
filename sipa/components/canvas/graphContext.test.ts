import { describe, it, expect } from 'vitest'
import { DependencyEndpointKind, DependencyOrigin, type Dependency, type EndpointRef } from '@pleiades/sdk'
import { expandableKeys, globalContext, neighbourPins, pinKey } from './graphContext'
import { freeSlotNear, type NodeBox } from './graphLayout'

// The direct-neighbour walk behind "Add with dependencies", "Expand Connections" and "Open in Planning": one edge away,
// both directions, intrinsic edges included, deduplicated, skipping what is shown, and named by the resolver.

const { Directive, Objective, Fate, Eventive, Checkpoint } = DependencyEndpointKind

let nextId = 1

/** An edge as the listing serves it: flattened endpoint columns, an absent recurrence written as `null`. */
function edge(source: EndpointRef, target: EndpointRef, origin = DependencyOrigin.Explicit): Dependency {
	return {
		id: origin === DependencyOrigin.Intrinsic ? 0 : nextId++,
		origin,
		sourceKind: source.kind,
		sourceId: source.id,
		sourceRecurrenceId: source.recurrenceId ?? null,
		targetKind: target.kind,
		targetId: target.id,
		targetRecurrenceId: target.recurrenceId ?? null,
		trigger: undefined,
		constraint: undefined,
		satisfied: false,
		gatesNextTransition: false,
		reliedOn: false
	} as unknown as Dependency
}

const directive: EndpointRef = { kind: Directive, id: 'd1' }
const parent: EndpointRef = { kind: Directive, id: 'd0' }
const child: EndpointRef = { kind: Directive, id: 'd2' }
const objective: EndpointRef = { kind: Objective, id: 'o1' }
const fate: EndpointRef = { kind: Fate, id: 'f1' }
const checkpoint: EndpointRef = { kind: Checkpoint, id: 'c1' }
const far: EndpointRef = { kind: Objective, id: 'o9' }

const titles: Record<string, string> = { 'Directive:d0': 'Parent', 'Directive:d2': 'Child', 'Objective:o1': 'Objective one', 'Fate:f1': 'A fate' }
const resolve = (kind: DependencyEndpointKind, id: string) => {
	const title = titles[pinKey({ kind, id })]
	return title ? { id, title } : undefined
}

describe('neighbourPins', () => {
	const graph = [
		edge(objective, directive), // a prerequisite of the directive
		edge(directive, checkpoint), // a dependant of the directive
		edge(child, directive, DependencyOrigin.Intrinsic), // an open child feeds the directive's finish
		edge(directive, parent, DependencyOrigin.Intrinsic), // and the directive feeds its parent's
		edge(far, objective) // two edges away: not a direct neighbour
	]

	it('walks both directions and the intrinsic edges, but not the chain', () => {
		const pins = neighbourPins(graph, directive)
		expect(pins.map(pinKey)).toEqual(['Objective:o1', 'Checkpoint:c1', 'Directive:d2', 'Directive:d0'])
	})

	it('names each pin through the resolver, falling back to its id', () => {
		const pins = neighbourPins(graph, directive, new Set(), resolve)
		expect(pins.map(pin => pin.title)).toEqual(['Objective one', 'c1', 'Child', 'Parent'])
		expect(pins.find(pin => pin.id === 'c1')).toEqual({ kind: Checkpoint, id: 'c1', title: 'c1' })
	})

	it('skips what is already shown', () => {
		const present = new Set(['Objective:o1', 'Directive:d0'])
		expect(neighbourPins(graph, directive, present).map(pinKey)).toEqual(['Checkpoint:c1', 'Directive:d2'])
	})

	it('lists a neighbour once, however many edges join it, and never the endpoint itself', () => {
		const doubled = [
			edge(objective, directive),
			edge(directive, objective), // the other way round: a begin/finish pairing the ordering allows
			edge(objective, directive),
			edge(directive, directive)
		]
		expect(neighbourPins(doubled, directive).map(pinKey)).toEqual(['Objective:o1'])
	})

	it('pins an eventive neighbour under its owner, its slot dropped, once for all its occurrences', () => {
		const occurrences = [
			edge({ kind: Eventive, id: 'f1', recurrenceId: '2026-11-01T09:00:00' }, directive),
			edge({ kind: Eventive, id: 'f1', recurrenceId: '2026-11-08T09:00:00' }, directive)
		]
		expect(neighbourPins(occurrences, directive)).toEqual([{ kind: Eventive, id: 'f1', title: 'f1' }])
	})

	it('reads an edge on one occurrence as touching that occurrence, not its owner', () => {
		const graph = [edge({ kind: Eventive, id: 'f1', recurrenceId: '2026-11-01T09:00:00' }, directive)]
		expect(neighbourPins(graph, { kind: Eventive, id: 'f1' })).toEqual([])
		expect(neighbourPins(graph, { kind: Eventive, id: 'f1', recurrenceId: '2026-11-01T09:00:00' }).map(pinKey)).toEqual(['Directive:d1'])
	})

	it('reaches a fate or a checkpoint as readily as a directive', () => {
		const graph = [edge(fate, checkpoint), edge(checkpoint, objective)]
		expect(neighbourPins(graph, checkpoint).map(pinKey)).toEqual(['Fate:f1', 'Objective:o1'])
		expect(neighbourPins(graph, fate).map(pinKey)).toEqual(['Checkpoint:c1'])
	})

	it('shows every edge to the endpoint once its pins join a global context', () => {
		const pins = [{ ...directive, title: 'The directive' }, ...neighbourPins(graph, directive, new Set(), resolve)]
		const context = globalContext(pins, graph)
		expect(context.nodes.map(node => node.key)).toEqual(['Directive:d1', 'Objective:o1', 'Checkpoint:c1', 'Directive:d2', 'Directive:d0'])
		expect(context.edges).toHaveLength(4)
	})
})

describe('expandableKeys', () => {
	it('names the shown endpoints with a neighbour not shown yet, as neighbourPins finds them', () => {
		const graph = [edge(objective, directive), edge(directive, checkpoint), edge(child, directive, DependencyOrigin.Intrinsic), edge(far, objective)]
		const present = new Set(['Directive:d1', 'Objective:o1', 'Checkpoint:c1', 'Directive:d2'])
		const keys = expandableKeys(graph, present)
		expect([...keys]).toEqual(['Objective:o1'])
		for (const key of present) {
			const [kind, id] = key.split(':') as [keyof typeof DependencyEndpointKind, string]
			expect(keys.has(key)).toBe(neighbourPins(graph, { kind: DependencyEndpointKind[kind], id }, present).length > 0)
		}
	})

	it('names nothing once every neighbour is shown', () => {
		const graph = [edge(objective, directive), edge(directive, checkpoint)]
		expect(expandableKeys(graph, new Set(['Directive:d1', 'Objective:o1', 'Checkpoint:c1'])).size).toBe(0)
	})
})

describe('freeSlotNear', () => {
	const box = (x: number, y: number): NodeBox => ({ x, y, width: 240, height: 76 })

	it('keeps the wanted place when nothing covers it', () => {
		expect(freeSlotNear(box(0, 0), [box(400, 0)], 32)).toEqual(box(0, 0))
	})

	it('steps below, then above, past what covers it', () => {
		expect(freeSlotNear(box(0, 0), [box(0, 0)], 32)).toEqual(box(0, 108))
		expect(freeSlotNear(box(0, 0), [box(0, 0), box(0, 108)], 32)).toEqual(box(0, -108))
	})

	it('finds a place past a column of taken boxes', () => {
		const column = Array.from({ length: 5 }, (_, index) => box(10, (index - 2) * 108))
		const slot = freeSlotNear(box(0, 0), column, 32)
		expect(column.some(taken => Math.abs(taken.y - slot.y) < 108)).toBe(false)
	})
})
