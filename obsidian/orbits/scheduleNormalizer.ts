// Stage 1: AST -> ScheduleModel. "Normalization" in NLG terms — read the parse tree and produce the
// notation-independent meaning. Everything downstream (any register of humaniser, CalDAV export, a
// "next occurrence" blurb) can share this instead of re-walking the tree.
//
// The walk is deliberately linear: an Orbit chain is a spine of TimeUnitNodes (w -> d -> h -> ...), each
// narrowing the one above. We collect calendar units (y/M/w/d) as `frames`, fold the h/m/s tail into a
// clock `time`, and pick up `bounds` (limits) and `span` (duration) wherever they hang. Set-operations
// (`+ & ^ -`) are the one non-linear shape, so they become a `compound` node the realizer can still voice.
// A z/Z datetime literal is its own leaf: a dated one is a fixed `instant`, a bare time one a daily clock.

import type { ASTNode, DateTimeLiteralNode, DurationPart, IndexSpec, LimitSpec, TimeUnitNode } from './ast'
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
	if (node.kind === 'DateTimeLiteralNode') {
		return normalizeLiteral(node, calendar)
	}
	return normalizeChain(node, calendar)
}

/**
 * A z/Z literal. A dated one (`Z{…}`, year present) is a fixed {@link InstantSchedule}; a bare time one
 * (`z{h:m}`, no date) is just a daily clock — a frameless simple schedule with only a `time`.
 */
function normalizeLiteral(node: DateTimeLiteralNode, calendar: CalendarSystem): ScheduleModel {
	const bounds = node.limits.map(toBound)
	const span = node.duration && node.duration.length > 0 ? node.duration : undefined
	const time = readLiteralClock(node)

	if (node.year !== undefined) {
		return { kind: 'instant', year: node.year, month: named(node.month!, 'M', 'y', calendar), day: node.day!, time, span, bounds }
	}
	return { kind: 'simple', frames: [], time, span, bounds }
}

function normalizeChain(root: TimeUnitNode, calendar: CalendarSystem): ScheduleModel {
	const frames: Frame[] = []
	const bounds: Bound[] = []
	let time: ClockTime | undefined
	let span: DurationPart[] | undefined

	let node: ASTNode | undefined = root
	let enclosing: CalendarUnit | undefined

	while (node) {
		if (node.kind === 'SetOperationNode') {
			// A set-operation nested inside the chain (e.g. d[h{1}+h{3}]). Rare; defer to the legacy humaniser.
			throw new UnsupportedShapeError('set operation inside a chain')
		}

		// Bounds and span can hang off any node in the spine; gather them wherever they are.
		for (const limit of node.limits) bounds.push(toBound(limit))
		if (node.duration && node.duration.length > 0) span = node.duration

		if (node.kind === 'DateTimeLiteralNode') {
			// A time-only z{h:m} nested as the tail is the clock; a dated literal cannot sit mid-chain.
			if (node.year !== undefined) throw new UnsupportedShapeError('dated literal inside a chain')
			time = readLiteralClock(node)
			break
		}

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

function toBound(limit: LimitSpec): Bound {
	switch (limit.type) {
		case 'instances': return { kind: 'count', times: limit.count }
		case 'iterations': return { kind: 'perCycle', times: limit.count }
		case 'after': return { kind: 'after', at: limit.timestamp }
		case 'before': return { kind: 'before', at: limit.timestamp }
	}
}

/** Reads the clock components of a z/Z literal into a {@link ClockTime}, or undefined when it has no time part. */
function readLiteralClock(node: DateTimeLiteralNode): ClockTime | undefined {
	if (node.hour === undefined) return undefined
	return { hours: [node.hour], minutes: [node.minute ?? 0], seconds: node.second !== undefined ? [node.second] : [] }
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

/** Pairs a numeric index with the calendar's name for it (weekday, month) and its curated short name, when they exist. */
function named(value: number, unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): NamedValue {
	const name = calendar.getUnitName(unit, value, parent) ?? undefined
	if (!name) return { value }
	const shortName = calendar.getUnitShortName?.(unit, value, parent) ?? undefined
	return shortName ? { value, name, shortName } : { value, name }
}
