// Stage 1: AST -> ScheduleModel. "Normalization" in NLG terms — read the parse tree and produce the
// notation-independent meaning. Everything downstream (any register of humaniser, CalDAV export, a
// "next occurrence" blurb) can share this instead of re-walking the tree.
//
// The walk is deliberately linear: an Orbit chain is a spine of TimeUnitNodes (w -> d -> h -> ...), each
// narrowing the one above. We collect calendar units (y/M/w/d) as `frames`, fold the h/m/s tail into a
// clock `time`, and pick up `bounds` (limits) and `span` (duration) wherever they hang. Set-operations
// (`+ & ^ -`) are the one non-linear shape, so they become a `compound` node the realizer can still voice.

import type { ASTNode, IndexSpec, LimitSpec, TimeUnitNode } from './ast'
import type { CalendarSystem } from './calendar'
import {
	Bound, CalendarUnit, ClockTime, Frame, NamedValue, ScheduleModel, Selection,
	isClockUnit, naturalParent,
} from './scheduleModel'

/** A shape this first pass does not model yet; the caller falls back to the legacy humaniser for it. */
export class UnsupportedShapeError extends Error {}

/** Reads an Orbit AST into its meaning. Throws {@link UnsupportedShapeError} for shapes not yet modelled. */
export function normalizeSchedule(node: ASTNode, calendar: CalendarSystem): ScheduleModel {
	if (node.kind === 'SetOperationNode') {
		return {
			kind: 'compound',
			operator: node.operator,
			left: normalizeSchedule(node.left, calendar),
			right: normalizeSchedule(node.right, calendar),
		}
	}
	return normalizeChain(node, calendar)
}

function normalizeChain(root: TimeUnitNode, calendar: CalendarSystem): ScheduleModel {
	const frames: Frame[] = []
	const bounds: Bound[] = []
	let time: ClockTime | undefined
	let span: TimeUnitNode['duration']

	let node: ASTNode | undefined = root
	let enclosing: CalendarUnit | undefined

	while (node) {
		if (node.kind !== 'TimeUnitNode') {
			// A set-operation nested inside the chain (e.g. d[h{1}+h{3}]). Rare; defer to the legacy humaniser.
			throw new UnsupportedShapeError('set operation inside a chain')
		}

		// Bounds and span can hang off any node in the spine; gather them wherever they are.
		for (const limit of node.limits) bounds.push(toBound(limit))
		span = pickSpan(node, span)

		if (isClockUnit(node.unit)) {
			time = readClock(node)   // the h/m/s tail is the time of day; nothing calendar follows it
			break
		}

		const unit = node.unit as CalendarUnit
		// A `d` nested under `w` is a weekday; otherwise the enclosing (or natural) parent applies.
		const parent = enclosing ?? naturalParent(unit)
		frames.push({ unit, parent, interval: node.interval, selection: toSelection(node.indices, unit, parent, calendar) })
		enclosing = unit
		node = node.child
	}

	return { kind: 'simple', frames, time, span, bounds }
}

function pickSpan(node: TimeUnitNode, current: TimeUnitNode['duration']): TimeUnitNode['duration'] {
	return node.duration && node.duration.length > 0 ? node.duration : current
}

function toBound(limit: LimitSpec): Bound {
	switch (limit.type) {
		case 'instances': return { kind: 'count', times: limit.count }
		case 'iterations': return { kind: 'perCycle', times: limit.count }
		case 'after': return { kind: 'after', at: limit.timestamp }
		case 'before': return { kind: 'before', at: limit.timestamp }
	}
}

/** Reads an `h{…}[m{…}[s{…}]]` clock tail into a {@link ClockTime}. Only the list form is modelled here. */
function readClock(hNode: TimeUnitNode): ClockTime {
	if (!hNode.indices || hNode.indices.type !== 'list') {
		// A bare `h` (hourly) or a fancy hour index is a selection, not a wall-clock reading — defer it.
		throw new UnsupportedShapeError('non-clock time unit')
	}

	const hours = hNode.indices.values
	let minutes = [0]
	let seconds: number[] = []

	let current: ASTNode | undefined = hNode.child
	if (current?.kind === 'TimeUnitNode' && current.unit === 'm' && current.indices?.type === 'list') {
		minutes = current.indices.values
		current = current.child
	}
	if (current?.kind === 'TimeUnitNode' && current.unit === 's' && current.indices?.type === 'list') {
		seconds = current.indices.values
	}

	return { hours, minutes, seconds }
}

function toSelection(indices: IndexSpec | undefined, unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): Selection {
	if (!indices) return { kind: 'all' }

	switch (indices.type) {
		case 'random':
			return { kind: 'random', count: indices.count }
		case 'range':
			return { kind: 'range', start: named(indices.start, unit, parent, calendar), end: named(indices.end, unit, parent, calendar) }
		case 'list':
			return { kind: 'list', values: indices.values.map(value => named(value, unit, parent, calendar)) }
	}
}

/** Pairs a numeric index with the calendar's name for it (weekday, month), when one exists. */
function named(value: number, unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): NamedValue {
	const name = calendar.getUnitName(unit, value, parent) ?? undefined
	return name ? { value, name } : { value }
}
