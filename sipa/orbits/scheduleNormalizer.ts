// Stage 1: AST -> ScheduleModel. "Normalization" in NLG terms — read the parse tree and produce the
// notation-independent meaning. Everything downstream (any register of humaniser, CalDAV export, a
// "next occurrence" blurb) can share this instead of re-walking the tree.
//
// The walk is deliberately linear: an Orbit chain is a spine of TimeUnitNodes (w -> d -> h -> ...), each
// narrowing the one above. We collect calendar units (y/M/w/d) as `frames`, fold the h/m/s tail into a
// clock `time`, and pick up `bounds` (`<t >t`) and `span` (duration) wherever they hang. Set-operations
// (`+ & ^ -`) are the one non-linear shape, so they become a `compound` node the realizer can still voice.
// A z/Z datetime literal is its own leaf: a dated one is a fixed `instant`, a bare time one a daily clock.
//
// A node's own limits are read against that node, as the engine resolves them: `*x` caps its repetition — the first x
// values of a bare unit, or the first x steps of each run of a `%`-stepped index, and nothing at all on an index `%`
// does not step — and `@x` caps its emission per period of its written parent (in all, at the top level). Where a
// limit can be said as a plainer selection it becomes one: `d{5}%3*4` is the 5th, 8th, 11th and 14th, `w[d*3]` the
// first 3 days of every week, and an `@x` on a nested list keeps its first x values (`d[h{9,12,15,18}@2]` is 09:00 and
// 12:00). A `%` on a weekday or a month is spelled out the same way, since it names a handful of values.

import type { ASTNode, DateTimeLiteralNode, DurationPart, IndexSpec, LimitSpec, TimeUnit, TimeUnitNode } from './ast'
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
	const bounds = windowBounds(node.limits)
	const span = node.duration && node.duration.length > 0 ? node.duration : undefined

	if (node.year !== undefined) {
		// A dated literal's modifiers ride on its year, a single moment: an index `%` does not step, so its `*x` means
		// nothing, and its `@x` caps what already fires once.
		const time = node.hour === undefined ? undefined : { hours: [node.hour], minutes: [node.minute ?? 0], seconds: node.second !== undefined ? [node.second] : [] }
		return { kind: 'instant', year: node.year, month: named(node.month!, 'M', 'y', calendar), day: node.day!, time, span, bounds }
	}
	return { kind: 'simple', frames: [], time: readLiteralClock(node, undefined, bounds), span, bounds }
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

		// Window bounds and the span can hang off any node in the spine; gather them wherever they are.
		bounds.push(...windowBounds(node.limits))
		if (node.duration && node.duration.length > 0) span = node.duration

		if (node.kind === 'DateTimeLiteralNode') {
			// A time-only z{h:m} nested as the tail is the clock; a dated literal cannot sit mid-chain.
			if (node.year !== undefined) throw new UnsupportedShapeError('dated literal inside a chain')
			time = readLiteralClock(node, enclosing, bounds)
			break
		}

		if (isClockUnit(node.unit)) {
			// The h/m/s tail is the time of day; nothing calendar follows it. Its minute and second nodes can carry
			// window bounds and a span too, gathered like the spine's.
			time = readClock(node, enclosing, bounds, clockNode => {
				bounds.push(...windowBounds(clockNode.limits))
				if (clockNode.duration && clockNode.duration.length > 0) span = clockNode.duration
			})
			break
		}

		const unit = node.unit as CalendarUnit
		// A `d` nested under `w` is a weekday; otherwise the enclosing (or natural) parent applies.
		const parent = enclosing ?? naturalParent(unit)
		frames.push(readFrame(node, unit, parent, enclosing, bounds, calendar))
		enclosing = unit
		node = node.child
	}

	return { kind: 'simple', frames, time, span, bounds }
}

/**
 * One calendar node as a frame, its limits read against it. `written` is the unit of the node's written parent, or
 * undefined for the top-level node, whose `@x` counts in all and whose `*x` runs the whole schedule.
 */
function readFrame(node: TimeUnitNode, unit: CalendarUnit, parent: CalendarUnit | undefined, written: CalendarUnit | undefined, bounds: Bound[], calendar: CalendarSystem): Frame {
	const repeat = limitOf(node.limits, 'iterations')
	const emit = limitOf(node.limits, 'instances')
	if (repeat === 0 || emit === 0) throw new UnsupportedShapeError('a limit that lets nothing fire')
	const leaf = !node.child
	const frame: Frame = { unit, parent, interval: node.interval, selection: toSelection(node.indices, unit, parent, calendar) }
	const stepped = (frame.interval ?? 1) > 1
	const values = staticValues(node.indices)

	if (values && stepped && (repeat !== undefined || isNamedDomain(unit, parent, calendar) || (emit !== undefined && leaf && written !== undefined))) {
		// A stepped index keeps the first x steps of each run (`d{5}%3*4`); a stepped weekday or month names its runs
		// outright (`w[d{1}%2]` is Monday, Wednesday, Friday and Sunday). Either way it reads as the values it lands on.
		frame.selection = listOf(expandRuns(values, frame.interval!, repeat, unitCeiling(unit, parent, calendar)), unit, parent, calendar)
		frame.interval = undefined
	} else if (!node.indices && repeat !== undefined) {
		if (written === undefined) frame.repeat = repeat
		else if (!stepped) frame.selection = firstOf(repeat, unit, parent, calendar)
		else if (leaf) bounds.push({ kind: 'count', times: repeat, per: written })
		else frame.repeat = repeat
	} else if (node.indices?.type === 'random' && stepped && repeat !== undefined) {
		throw new UnsupportedShapeError('a stepped random index with *x')
	}
	// Any other `*x` sits on an index `%` does not step: a run of one value, so it means nothing.

	if (emit !== undefined) {
		const listed = staticSelectionValues(frame.selection)
		if (written !== undefined && leaf && listed && !frame.interval) {
			// A nested list's first x values are the ones that fire in every period; a run of them reads as a range.
			const kept = listed.slice(0, Math.max(0, emit))
			frame.selection = kept.length >= 3 && kept[kept.length - 1]! - kept[0]! === kept.length - 1
				? { kind: 'range', start: named(kept[0]!, unit, parent, calendar), end: named(kept[kept.length - 1]!, unit, parent, calendar) }
				: listOf(kept, unit, parent, calendar)
		} else if (written !== undefined && leaf && frame.selection.kind === 'all' && !frame.interval && frame.repeat === undefined) {
			frame.selection = firstOf(emit, unit, parent, calendar)
		} else if (written !== undefined && leaf && frame.selection.kind === 'first' && !frame.interval) {
			frame.selection = firstOf(Math.min(emit, frame.selection.count), unit, parent, calendar)
		} else {
			bounds.push(written === undefined ? { kind: 'count', times: emit } : { kind: 'count', times: emit, per: written })
		}
	}
	return frame
}

/** The window bounds (`<t >t`) among a node's limits; `*x` and `@x` belong to their node and are read with it. */
function windowBounds(limits: LimitSpec[]): Bound[] {
	const bounds: Bound[] = []
	for (const limit of limits) {
		if (limit.type === 'after') bounds.push({ kind: 'after', at: limit.timestamp })
		if (limit.type === 'before') bounds.push({ kind: 'before', at: limit.timestamp })
	}
	return bounds
}

/** A node's tightest limit of one kind, as the engine reads a repeated `*x`/`@x`. */
function limitOf(limits: LimitSpec[], type: 'iterations' | 'instances'): number | undefined {
	let tightest: number | undefined
	for (const limit of limits) {
		if (limit.type === type) tightest = tightest === undefined ? limit.count : Math.min(tightest, limit.count)
	}
	return tightest
}

/**
 * Reads a time-only z/Z literal into a {@link ClockTime}, or undefined when it has no time part. Its modifiers hang
 * on the hour it stands for, so its `%N` steps the hour (`z{09:00}%2` is every 2 hours from 09:00), its `*x` keeps
 * that many steps, and its `@x` caps its moments per period of `written` (in all at the top level).
 */
function readLiteralClock(node: DateTimeLiteralNode, written: CalendarUnit | undefined, bounds: Bound[]): ClockTime | undefined {
	if (node.hour === undefined) return undefined
	const seconds: TimeUnitNode | undefined = node.second === undefined
		? undefined
		: { kind: 'TimeUnitNode', unit: 's', indices: { type: 'list', values: [node.second] }, limits: [] }
	const minutes: TimeUnitNode = { kind: 'TimeUnitNode', unit: 'm', indices: { type: 'list', values: [node.minute ?? 0] }, limits: [], child: seconds }
	const hour: TimeUnitNode = {
		kind: 'TimeUnitNode', unit: 'h', indices: { type: 'list', values: [node.hour] }, interval: node.interval,
		limits: node.limits.filter(limit => limit.type === 'iterations' || limit.type === 'instances'), child: minutes,
	}
	return readClock(hour, written, bounds, () => {})
}

/**
 * Reads an `h{…}[m{…}[s{…}]]` clock tail into a {@link ClockTime}, handing each minute and second node to `gather`
 * for its window bounds and span. The hours must be listed. A minute or second node may be listed or bare, and a
 * `%N` on any of the three becomes the time's {@link ClockStep}; a bare minute or second streams, so it must be the
 * finest unit. Each node's `*x`/`@x` is read against it (see {@link readClockValues}). Any other shape — a bare or
 * ranged hour, a ranged or random minute, a second clock step — is deferred rather than read partly, since reading it
 * partly would drop what it says.
 */
function readClock(hNode: TimeUnitNode, written: CalendarUnit | undefined, bounds: Bound[], gather: (node: TimeUnitNode) => void): ClockTime {
	if (!hNode.indices || hNode.indices.type !== 'list') {
		// A bare `h` (hourly) or a fancy hour index is a selection, not a wall-clock reading — defer it.
		throw new UnsupportedShapeError('non-clock time unit')
	}

	const steps: ClockStep[] = []
	const hours = readClockValues(hNode, written, bounds, steps)
	let minutes = [0]
	let seconds: number[] = []

	let current: ASTNode | undefined = hNode.child
	if (current) {
		const minuteNode = clockChild(current, 'm')
		gather(minuteNode)
		minutes = readClockValues(minuteNode, 'h', bounds, steps)
		current = minuteNode.child
	}
	if (current) {
		if (minutes.length === 0) throw new UnsupportedShapeError('seconds under streaming minutes')
		const secondNode = clockChild(current, 's')
		gather(secondNode)
		seconds = readClockValues(secondNode, 'm', bounds, steps)
		current = secondNode.child
	}
	if (current) throw new UnsupportedShapeError('a node below the seconds')
	if (steps.length > 1) throw new UnsupportedShapeError('more than one clock step')
	if (hours.length === 0 || (hNode.child && minutes.length === 0 && !steps.some(step => step.unit === 'm'))) {
		throw new UnsupportedShapeError('a clock with nothing left to fire')
	}

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
 * An hour, minute or second node's listed values, pushing its step onto `steps`: a listed node steps on a `%N`, and a
 * bare node always streams (every 1 of the unit, or every N with a `%N`), reading as no listed values. Its `*x` keeps
 * the first x steps of each run (a stepped list is spelled out; a streaming unit carries it as the step's `times`),
 * and its `@x` keeps the first x values when it is the finest unit under a written parent, else caps it per period of
 * `per` (in all at the top level).
 */
function readClockValues(node: TimeUnitNode, per: TimeUnit | undefined, bounds: Bound[], steps: ClockStep[]): number[] {
	const unit = node.unit as ClockStep['unit']
	const interval = node.interval ?? 1
	const repeat = limitOf(node.limits, 'iterations')
	const emit = limitOf(node.limits, 'instances')
	if (repeat === 0 || emit === 0) throw new UnsupportedShapeError('a limit that lets nothing fire')
	const leaf = !node.child
	const ceiling = unit === 'h' ? 23 : 59

	if (node.indices?.type === 'list') {
		let values = [...new Set(node.indices.values)].sort((a, b) => a - b)
		let stepped = interval > 1
		if (stepped && (repeat !== undefined || (emit !== undefined && leaf && per !== undefined))) {
			values = expandRuns(values, interval, repeat, ceiling)
			stepped = false
		}
		if (emit !== undefined) {
			if (leaf && per !== undefined && !stepped) values = values.slice(0, Math.max(0, emit))
			else bounds.push(per === undefined ? { kind: 'count', times: emit } : { kind: 'count', times: emit, per })
		}
		if (stepped) steps.push({ unit, interval })
		return values
	}
	if (!node.indices) {
		let times = repeat
		if (emit !== undefined) {
			if (leaf && per !== undefined) times = times === undefined ? emit : Math.min(times, emit)
			else bounds.push(per === undefined ? { kind: 'count', times: emit } : { kind: 'count', times: emit, per })
		}
		steps.push(times === undefined ? { unit, interval } : { unit, interval, times })
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

/** The values a list or range index names, sorted; undefined for a random or absent index. */
function staticValues(indices: IndexSpec | undefined): number[] | undefined {
	if (indices?.type === 'list') return [...new Set(indices.values)].sort((a, b) => a - b)
	if (indices?.type === 'range') {
		const values: number[] = []
		for (let value = indices.start; value <= indices.end; value++) values.push(value)
		return values
	}
	return undefined
}

/** The values a list or range selection names, sorted; undefined for any other selection. */
function staticSelectionValues(selection: Selection): number[] | undefined {
	if (selection.kind === 'list') return [...new Set(selection.values.map(value => value.value))].sort((a, b) => a - b)
	if (selection.kind === 'range') {
		const values: number[] = []
		for (let value = selection.start.value; value <= selection.end.value; value++) values.push(value)
		return values
	}
	return undefined
}

/** Every run of a stepped index — from each value b: b, b+N, … up to `ceiling`, the first `times` of each — merged and sorted. */
function expandRuns(values: number[], interval: number, times: number | undefined, ceiling: number): number[] {
	const out = new Set<number>()
	for (const base of values) {
		for (let step = 0, value = base; value <= ceiling && (times === undefined || step < times); step++, value += interval) out.add(value)
	}
	return [...out].sort((a, b) => a - b)
}

function listOf(values: number[], unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): Selection {
	return { kind: 'list', values: values.map(value => named(value, unit, parent, calendar)) }
}

/** The first `count` values of a bare unit within its parent — its values run from 1. */
function firstOf(count: number, unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): Selection {
	return { kind: 'first', count, start: named(1, unit, parent, calendar), end: named(Math.max(1, count), unit, parent, calendar) }
}

/** Whether a unit is one the calendar names (a weekday, a month): a handful of values, so a stepped index is spelled out. */
function isNamedDomain(unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): boolean {
	return calendar.getUnitName(unit, 1, parent) !== null
}

/**
 * The largest value a unit can take within its parent, so a spelled-out run stops where the calendar does: a named
 * unit runs as far as the calendar names it (seven weekdays, twelve or six months); a day or week of the month as far
 * as the calendar's longest month reaches.
 */
function unitCeiling(unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): number {
	if (isNamedDomain(unit, parent, calendar)) {
		let value = 1
		while (value < 400 && calendar.getUnitName(unit, value + 1, parent) !== null) value++
		return value
	}
	if (parent === 'y') return unit === 'w' ? 53 : 366
	if (unit === 'y') return 9999
	let ceiling = 0
	for (let month = 0; month < 12; month++) ceiling = Math.max(ceiling, calendar.max(unit, new Date(Date.UTC(2024, month, 1))))
	return ceiling
}

/** Pairs a numeric index with the calendar's name for it (weekday, month) and its curated short name, when they exist. */
function named(value: number, unit: CalendarUnit, parent: CalendarUnit | undefined, calendar: CalendarSystem): NamedValue {
	const name = calendar.getUnitName(unit, value, parent) ?? undefined
	if (!name) return { value }
	const shortName = calendar.getUnitShortName?.(unit, value, parent) ?? undefined
	return shortName ? { value, name, shortName } : { value, name }
}
