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
	Bound, CalendarUnit, ClockStep, ClockTime, Frame, NamedValue, ScheduleModel, Selection,
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
			// The h/m/s tail is the time of day; nothing calendar follows it. Its minute and second nodes can carry
			// bounds and a span too, gathered like the spine's.
			time = readClock(node, clockNode => {
				for (const limit of clockNode.limits) bounds.push(toBound(limit))
				if (clockNode.duration && clockNode.duration.length > 0) span = clockNode.duration
			})
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

/**
 * Reads the clock components of a z/Z literal into a {@link ClockTime}, or undefined when it has no time part. A
 * time-only `z{h:m}` hangs its modifiers on the hour it stands for, so its `%N` steps the hour (`z{09:00}%2` is every
 * 2 hours from 09:00). A dated `Z{…}` hangs them on its year instead, which is no clock step.
 */
function readLiteralClock(node: DateTimeLiteralNode): ClockTime | undefined {
	if (node.hour === undefined) return undefined
	const time: ClockTime = { hours: [node.hour], minutes: [node.minute ?? 0], seconds: node.second !== undefined ? [node.second] : [] }
	if (node.year === undefined && node.interval !== undefined && node.interval > 1) {
		time.step = { unit: 'h', interval: node.interval }
	}
	return time
}

/**
 * Reads an `h{…}[m{…}[s{…}]]` clock tail into a {@link ClockTime}, handing each minute and second node to `gather`
 * for its bounds and span. The hours must be listed. A minute or second node may be listed or bare, and a `%N` on any
 * of the three becomes the time's {@link ClockStep}; a bare minute or second streams, so it must be the finest unit.
 * Any other shape — a bare or ranged hour, a ranged or random minute, a second clock step — is deferred rather than
 * read partly, since reading it partly would drop what it says.
 */
function readClock(hNode: TimeUnitNode, gather: (node: TimeUnitNode) => void): ClockTime {
	if (!hNode.indices || hNode.indices.type !== 'list') {
		// A bare `h` (hourly) or a fancy hour index is a selection, not a wall-clock reading — defer it.
		throw new UnsupportedShapeError('non-clock time unit')
	}

	const steps: ClockStep[] = []
	if (hNode.interval !== undefined && hNode.interval > 1) steps.push({ unit: 'h', interval: hNode.interval })

	const hours = hNode.indices.values
	let minutes = [0]
	let seconds: number[] = []

	let current: ASTNode | undefined = hNode.child
	if (current) {
		const minuteNode = clockChild(current, 'm')
		gather(minuteNode)
		minutes = readClockValues(minuteNode, steps)
		current = minuteNode.child
	}
	if (current) {
		if (minutes.length === 0) throw new UnsupportedShapeError('seconds under streaming minutes')
		const secondNode = clockChild(current, 's')
		gather(secondNode)
		seconds = readClockValues(secondNode, steps)
		current = secondNode.child
	}
	if (current) throw new UnsupportedShapeError('a node below the seconds')
	if (steps.length > 1) throw new UnsupportedShapeError('more than one clock step')

	const time: ClockTime = { hours, minutes, seconds }
	if (steps.length === 1) time.step = steps[0]
	return time
}

/** The node under an hour or minute, which must be the next finer clock unit on its own (not a set-operation). */
function clockChild(node: ASTNode, unit: 'm' | 's'): TimeUnitNode {
	if (node.kind !== 'TimeUnitNode' || node.unit !== unit) throw new UnsupportedShapeError(`unmodelled clock child`)
	return node
}

/**
 * A minute or second node's listed values, pushing its step onto `steps`: a listed node steps on a `%N`, and a bare
 * node always streams (every 1 of the unit, or every N with a `%N`), reading as no listed values.
 */
function readClockValues(node: TimeUnitNode, steps: ClockStep[]): number[] {
	const unit = node.unit as ClockStep['unit']
	const interval = node.interval ?? 1
	if (node.indices?.type === 'list') {
		if (interval > 1) steps.push({ unit, interval })
		return node.indices.values
	}
	if (!node.indices) {
		steps.push({ unit, interval })
		return []
	}
	throw new UnsupportedShapeError(`unmodelled ${unit} selection`)
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
