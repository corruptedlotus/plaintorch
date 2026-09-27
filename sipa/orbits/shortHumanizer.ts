// Plugin-authored compact humanizer — a terse counterpart to the vendored OrbitHumanizer,
// for space-constrained frontend chips. This is NOT vendored: it is PLAINTORCH's own
// rendering (upstream has no short form), so it lives beside index.ts and follows the
// plugin's strict tsconfig rather than carrying @ts-nocheck.
//
// It mirrors the vendored humanizer's three shapes — the weekly-day shorthand, the clock
// shorthand, and the regular unit assembly — but emits an abbreviated, prefix-ordered
// reading ("Every 3 Wed @12:00") rather than the long child-first one ("at 12:00 of the 3rd
// Wednesday"). Anything it cannot shorten cleanly it renders as best it can; only truly
// unexpected input throws, and the caller (humanizeOrbit) then falls back to the full
// phrase — so exotic notation is never mangled, only ever rendered long.

import { ASTNode, DateTimeLiteralNode, IndexSpec, TimeUnit, TimeUnitNode } from './ast'
import { CalendarSystem } from './calendar'

/** Terse unit words; a trailing 's' is added where a count makes them plural. */
const UNIT_SHORT: Record<TimeUnit, string> = { y: 'yr', M: 'mo', w: 'wk', d: 'day', h: 'hr', m: 'min', s: 'sec' }

/** Terse connectors for set operations, standing in for "and also / that are also / excluding / differing from". */
const SET_JOIN: Record<'+' | '&' | '-' | '^', string> = { '+': ' + ', '&': ' & ', '-': ' − ', '^': ' ^ ' }

/** A clock reading pulled out of an `h[m[s]]` tail, plus whatever nesting sat beneath it. */
interface ClockReading {
	time: string
	remainingChild?: ASTNode
}

export class OrbitShortHumanizer {
	constructor(private readonly calendar: CalendarSystem) { }

	public serialize(node: ASTNode, isRoot = true): string {
		if (node.kind === 'SetOperationNode') {
			return `${this.serialize(node.left, isRoot)}${SET_JOIN[node.operator]}${this.serialize(node.right, isRoot)}`
		}
		if (node.kind === 'DateTimeLiteralNode') {
			return this.serializeDateTimeLiteral(node, isRoot)
		}
		return this.serializeUnit(node, isRoot)
	}

	/** A z/Z literal, terse: a daily clock ("daily @12:00" / "@12:00") or a fixed date ("5 Jun 2027 @18:00"). */
	private serializeDateTimeLiteral(node: DateTimeLiteralNode, isRoot: boolean): string {
		const time = node.hour !== undefined
			? (node.second !== undefined
				? `@${pad(node.hour)}:${pad(node.minute!)}:${pad(node.second)}`
				: `@${pad(node.hour)}:${pad(node.minute!)}`)
			: null
		if (node.year !== undefined) {
			const monthName = this.calendar.getUnitName('M', node.month!, 'y')
			const month = monthName ? monthName.slice(0, 3) : `${node.month}`
			const date = `${node.day} ${month} ${node.year}`
			return time ? `${date} ${time}` : date
		}
		return isRoot ? `daily ${time}` : time!
	}

	private serializeUnit(node: TimeUnitNode, isRoot: boolean): string {
		// Weekly day, e.g. w{1}[d{3}] -> "1st Wed", w%3[d{3}] -> "Every 3 Wed".
		const weekly = this.tryWeeklyDay(node)
		if (weekly) return weekly

		// Clock, e.g. h{12}[m{0}] -> "@12:00".
		const clock = this.tryClock(node)
		if (clock) {
			if (clock.remainingChild) return `${this.serialize(clock.remainingChild, false)} ${clock.time}`
			return isRoot ? `daily ${clock.time}` : clock.time
		}

		const text = this.assemble(node, isRoot)
		if (node.child) {
			const childText = this.serialize(node.child, false)
			// A time tail reads as a suffix ("Every mo @09:00"); any other nesting keeps the "X of Y" order.
			return childText.startsWith('@') ? `${text} ${childText}` : `${childText} of ${text}`
		}
		return text
	}

	/** `w …[d …]` with an optional trailing clock — the recurring-weekday shape, the one worth shortening most. */
	private tryWeeklyDay(node: TimeUnitNode): string | null {
		if (node.unit !== 'w') return null

		const child = node.child
		if (!child || child.kind !== 'TimeUnitNode' || child.unit !== 'd' || !child.indices || child.interval) return null

		const days = this.namesFromIndices(child.indices, 'd', 'w')
		if (!days) return null

		const week = this.weekPrefix(node)
		if (week === null) return null

		const label = week ? `${week} ${days}` : days
		// The weekday's own child is the clock, if any — appended as "@HH:MM".
		return child.child ? `${label} ${this.serialize(child.child, false)}` : label
	}

	/** The week qualifier before the day name: "Every", "Every 3", "1st", "1st & 3rd" — or null to defer to the long form. */
	private weekPrefix(node: TimeUnitNode): string | null {
		if (node.interval && node.interval > 1) {
			// Interval and explicit weeks together are rare and read poorly terse; let the long form take it.
			return node.indices ? null : `every ${node.interval}`
		}
		if (node.indices) {
			return this.ordinalsFromIndices(node.indices)
		}
		return 'every'
	}

	/** `h{…}[m{…}[s{…}]]` -> "@HH:MM[:SS]" (multiple values joined), matching the vendored clock lookahead. */
	private tryClock(node: TimeUnitNode): ClockReading | null {
		if (node.unit !== 'h' || !node.indices || node.indices.type !== 'list' || node.interval) return null

		const hours = node.indices.values
		let minutes: number[] = [0]
		let seconds: number[] = []
		let current: ASTNode | undefined = node.child
		let consumedM = false

		if (current && current.kind === 'TimeUnitNode' && current.unit === 'm' && !current.interval && current.indices?.type === 'list') {
			minutes = current.indices.values
			consumedM = true
			current = current.child
		}
		if (consumedM && current && current.kind === 'TimeUnitNode' && current.unit === 's' && !current.interval && current.indices?.type === 'list') {
			seconds = current.indices.values
			current = current.child
		}
		// A bare hour without a minute is not a clock reading (mirrors the vendored humanizer).
		if (!consumedM) return null

		const parts: string[] = []
		for (const h of hours) {
			for (const m of minutes) {
				parts.push(seconds.length > 0
					? seconds.map(s => `${pad(h)}:${pad(m)}:${pad(s)}`).join(', ')
					: `${pad(h)}:${pad(m)}`)
			}
		}
		return { time: `@${this.joinList(parts)}`, remainingChild: current }
	}

	/** The regular terse assembly for a single unit: "Every 3 wks", "Every day", "12th", "2 random days". */
	private assemble(node: TimeUnitNode, isRoot: boolean): string {
		const short = UNIT_SHORT[node.unit]
		const interval = node.interval && node.interval > 1 ? node.interval : undefined

		let indexText = ''
		if (node.indices) {
			const spec = node.indices
			if (spec.type === 'random') {
				indexText = `${spec.count} random ${short}${spec.count > 1 ? 's' : ''}`
			}
			else if (spec.type === 'range') {
				indexText = `${short} ${spec.start}–${spec.end}`
			}
			else {
				indexText = this.joinList(spec.values.map(v => this.toOrdinal(v)))
				// A plain numeric list needs its unit spelled out unless an interval already carries it.
				if (!interval) indexText += ` ${short}${spec.values.length > 1 ? 's' : ''}`
			}
		}

		if (interval && node.indices) return `every ${interval} ${short}s from ${indexText}`
		if (interval) return `every ${interval} ${short}s`
		if (node.indices) return indexText
		return isRoot ? `every ${short}` : short
	}

	/** Joins a list index of a named unit (weekday, month) to short names, or null if any value is unnamed. */
	private namesFromIndices(spec: IndexSpec, unit: TimeUnit, parent: TimeUnit): string | null {
		if (spec.type !== 'list') return null
		const names: string[] = []
		for (const value of spec.values) {
			const name = this.calendar.getUnitName(unit, value, parent)
			if (!name) return null
			names.push(name.slice(0, 3))
		}
		return this.joinList(names)
	}

	/** Joins a list index to ordinals ("1st", "1st & 3rd"), or null when the index is not a plain list. */
	private ordinalsFromIndices(spec: IndexSpec): string | null {
		if (spec.type !== 'list') return null
		return this.joinList(spec.values.map(value => this.toOrdinal(value)))
	}

	private joinList(list: string[]): string {
		if (list.length === 0) return ''
		if (list.length === 1) return list[0]!
		if (list.length === 2) return `${list[0]} & ${list[1]}`
		return `${list.slice(0, -1).join(', ')} & ${list[list.length - 1]}`
	}

	private toOrdinal(n: number): string {
		const suffixes = ['th', 'st', 'nd', 'rd']
		const v = n % 100
		return n + (suffixes[(v - 20) % 10] || suffixes[v] || suffixes[0]!)
	}
}

function pad(n: number): string {
	return n.toString().padStart(2, '0')
}
