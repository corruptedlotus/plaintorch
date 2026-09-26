// Stage 2: ScheduleModel -> phrase. "Realization" in NLG terms — turn meaning into words. This is the LONG
// register; a short/spoken register is the same walk with a different lexicon and ordering (a later step).
//
// Note what this file does NOT import: the calendar. Normalization already resolved weekday/month names into
// the model, so realization is pure meaning -> words. Bounds and durations, which the legacy humaniser
// dropped, are rendered here for free.
//
// The realizer recognises a few IDIOMS (peepholes over the frame list) for natural reading — a weekly
// weekday, an anchored calendar date, a day-of-month — and falls back to a generic "fine of coarse" chain
// for everything else.

import type { DurationPart, SetOperator } from './ast'
import type { Bound, CalendarUnit, ClockStep, ClockTime, CompoundSchedule, Frame, InstantSchedule, NamedValue, ScheduleModel, Selection, SimpleSchedule } from './scheduleModel'

const UNIT_WORD: Record<CalendarUnit, string> = { y: 'year', M: 'month', w: 'week', d: 'day' }
const CLOCK_WORD: Record<ClockStep['unit'], string> = { h: 'hour', m: 'minute', s: 'second' }
const DUR_WORD: Record<DurationPart['unit'], string> = { y: 'year', M: 'month', w: 'week', d: 'day', h: 'hour', m: 'minute', s: 'second' }

// Long-register set connectives: each reads as "<left><infix><right><suffix?>". A trailing clause (for
// symmetric difference) is a suffix so the sentence closes cleanly instead of wrapping around the operands.
const SET_CONNECTOR: Record<SetOperator, { infix: string; suffix?: string }> = {
	'+': { infix: ', and also ' },                                // union: the two streams combine
	'&': { infix: ', but only when it also falls on ' },          // intersection: only where they coincide
	'-': { infix: ', except ' },                                  // difference: A minus B
	'^': { infix: ', or ', suffix: ', but never both' },          // symmetric difference: one or the other
}

/** Renders a schedule's meaning as a full English phrase (long register). */
export function realizeLong(model: ScheduleModel): string {
	switch (model.kind) {
		case 'compound': return realizeCompound(model)
		case 'instant': return realizeInstant(model)
		case 'simple': return realizeSimple(model)
	}
}

function realizeCompound(model: CompoundSchedule): string {
	const { infix, suffix } = SET_CONNECTOR[model.operator]
	return `${realizeLong(model.left)}${infix}${realizeLong(model.right)}${suffix ?? ''}`
}

/** A fixed moment: "the 5th of June 2027", "the 5th of June 2027 at 18:00 for 2 hours". */
function realizeInstant(model: InstantSchedule): string {
	const monthName = model.month.name ?? `month ${model.month.value}`
	let text = `the ${ordinal(model.day)} of ${monthName} ${model.year}`
	if (model.time) text += clockClause(model.time)
	if (model.span && model.span.length > 0) text += ` for ${formatDuration(model.span)}`

	const bounds = model.bounds.map(phraseBound)
	if (bounds.length > 0) text += `, ${bounds.join(', ')}`
	return text
}

function realizeSimple(model: SimpleSchedule): string {
	let text = phraseFrames(model.frames)
	if (model.time) text += clockClause(model.time)
	if (model.span && model.span.length > 0) text += ` for ${formatDuration(model.span)}`

	const bounds = model.bounds.map(phraseBound)
	if (bounds.length > 0) text += `, ${bounds.join(', ')}`
	return text
}

// --- SHORT register: terse, spoken-but-compact ("Mon @5&16"). Same model + shared helpers, different lexicon
// (3-letter names, `@` clock, `&` joins, no "every"/"the") and tighter, prefix-first ordering. ---

const SHORT_DUR: Record<DurationPart['unit'], string> = { y: 'y', M: 'mo', w: 'w', d: 'd', h: 'h', m: 'm', s: 's' }
const SHORT_CADENCE: Record<CalendarUnit, string> = { y: 'yearly', M: 'monthly', w: 'weekly', d: 'daily' }

/** Renders a schedule's meaning as a compact, near-spoken phrase (short register). */
export function realizeShort(model: ScheduleModel): string {
	if (model.kind === 'compound') {
		return `${shortOperand(model.left)} ${model.operator} ${shortOperand(model.right)}`
	}
	if (model.kind === 'instant') {
		return shortInstant(model)
	}

	const parts: string[] = []
	const base = shortFrames(model.frames)
	if (base) parts.push(base)
	if (model.time) parts.push(...shortClockParts(model.time))
	if (model.span && model.span.length > 0) parts.push(`~${shortDuration(model.span)}`)
	for (const bound of model.bounds) parts.push(shortBound(bound))
	return parts.join(' ')
}

/** A set-operation operand, parenthesised when it is itself compound so the grouping stays legible. */
function shortOperand(model: ScheduleModel): string {
	const text = realizeShort(model)
	return model.kind === 'compound' ? `(${text})` : text
}

/** A fixed moment, terse: dates read day-first as "d/MMM y" — "5/Jun 2027", "5/Jun 2027 @18", "35/Xun 3 ~2h". */
function shortInstant(model: InstantSchedule): string {
	const parts = [`${model.day}/${shortLabel(model.month)} ${model.year}`]
	if (model.time) parts.push(...shortClockParts(model.time))
	if (model.span && model.span.length > 0) parts.push(`~${shortDuration(model.span)}`)
	for (const bound of model.bounds) parts.push(shortBound(bound))
	return parts.join(' ')
}

function shortFrames(frames: Frame[]): string {
	if (frames.length === 0) return 'daily'
	const coarsest = frames[0]!
	const finest = frames[frames.length - 1]!

	if (frames.length === 2 && coarsest.unit === 'w' && finest.unit === 'd' && finest.parent === 'w') {
		return shortWeekly(coarsest, finest)
	}
	const date = shortDate(frames)
	if (date) return date

	// A single selected frame over plain cadence: render just that selection. A unit-prefixed value ("w1") or
	// a day-of-month ("1st") stands alone, but a day-of-year ("d1") would be misread as a day-of-month, so it
	// takes a "/cadence" tag to disambiguate ("d1 /y").
	const selected = frames.filter(frame => frame.selection.kind !== 'all' || (frame.interval ?? 1) > 1)
	if (selected.length === 1) {
		const only = selected[0]!
		const payload = shortFrame(only)
		if (only.unit === 'd' && !isDayOfMonth(only) && coarsest.selection.kind === 'all' && coarsest !== only) {
			return `${payload} /${SHORT_DUR[coarsest.unit]}`
		}
		return payload
	}

	return frames.map(shortFrame).filter(part => part.length > 0).join(' ')
}

function shortWeekly(week: Frame, day: Frame): string {
	const days = shortSelection(day)
	if (week.selection.kind === 'all') {
		const interval = week.interval && week.interval > 1 ? week.interval : 1
		return interval === 1 ? days : `${days} /${interval}w`
	}
	const weekValue = onlyValue(week.selection)
	return weekValue ? `${ordinal(weekValue.value)} ${days}` : days
}

function shortDate(frames: Frame[]): string | null {
	const day = frames[frames.length - 1]!
	if (!isDayOfMonth(day) || (day.interval && day.interval > 1)) return null
	const dayValue = onlyValue(day.selection)
	if (!dayValue) return null
	const month = frames[frames.length - 2]
	if (!month || month.unit !== 'M') return null
	const monthValue = onlyValue(month.selection)
	if (!monthValue?.name) return null
	if (!frames.slice(0, frames.length - 2).every(frame => frame.unit === 'y' && frame.selection.kind === 'all')) return null
	// A recurring anchored date reads day-first too, but with no year ("5/Jun", "5/Tva").
	return `${dayValue.value}/${shortLabel(monthValue)}`
}

function shortFrame(frame: Frame): string {
	const interval = frame.interval && frame.interval > 1 ? frame.interval : undefined
	if (frame.selection.kind === 'all') {
		return interval ? `/${interval}${SHORT_DUR[frame.unit]}` : SHORT_CADENCE[frame.unit]
	}
	const body = shortSelection(frame)
	return interval ? `${body} /${interval}${SHORT_DUR[frame.unit]}` : body
}

/**
 * A frame's values, compactly: clipped names (Mon, Jun); ordinals for a day-of-month (1st); otherwise the value
 * carries its unit letter so it is not a bare, meaningless number ("w1", "y2027", "d1" for a day-of-year).
 */
function shortSelection(frame: Frame): string {
	const sel = frame.selection
	const dayOfMonth = isDayOfMonth(frame)
	const prefix = SHORT_DUR[frame.unit]
	if (sel.kind === 'all') return SHORT_CADENCE[frame.unit]
	if (sel.kind === 'random') return `${sel.count}?${prefix}`
	if (sel.kind === 'range') {
		if (sel.start.name && sel.end.name) return `${shortLabel(sel.start)}-${shortLabel(sel.end)}`
		return dayOfMonth ? `${ordinal(sel.start.value)}-${ordinal(sel.end.value)}` : `${prefix}${sel.start.value}-${sel.end.value}`
	}
	return sel.values.map(value =>
		value.name ? shortLabel(value) : (dayOfMonth ? ordinal(value.value) : `${prefix}${value.value}`)
	).join('&')
}

/** The clock, terse: "@9", and a step after it as its cadence ("@9 /2h", "@9 /15m" within the 9 o'clock hour). */
function shortClockParts(time: ClockTime): string[] {
	const clock = `@${shortClock(time)}`
	return time.step ? [clock, `/${time.step.interval}${SHORT_DUR[time.step.unit]}`] : [clock]
}

function shortClock(time: ClockTime): string {
	// Streaming minutes leave only the hours to name ("@9" for every 15 minutes within the 9 o'clock hour).
	if (time.minutes.length === 0) return time.hours.map(String).join('&')

	const parts: string[] = []
	for (const hour of time.hours) {
		for (const minute of time.minutes) {
			if (time.seconds.length > 0) {
				for (const second of time.seconds) parts.push(`${hour}:${pad(minute)}:${pad(second)}`)
			} else {
				parts.push(minute === 0 ? `${hour}` : `${hour}:${pad(minute)}`)
			}
		}
	}
	return parts.join('&')
}

function shortDuration(parts: DurationPart[]): string {
	return parts.map(part => `${part.count}${SHORT_DUR[part.unit]}`).join('')
}

function shortBound(bound: Bound): string {
	switch (bound.kind) {
		case 'count': return `×${bound.times}`
		case 'perCycle': return `×${bound.times}/cyc`
		case 'after': return `>${bound.at}`
		case 'before': return `<${bound.at}`
	}
}

function clip(name: string): string {
	return name.slice(0, 3)
}

/** A named value's short label: the calendar's curated short name, else a 3-letter clip of the full name. */
function shortLabel(value: NamedValue): string {
	if (value.shortName) return value.shortName
	if (value.name) return clip(value.name)
	return String(value.value)
}

function phraseFrames(frames: Frame[]): string {
	if (frames.length === 0) return 'every day' // time-only, e.g. z{09:00}

	const coarsest = frames[0]!
	const finest = frames[frames.length - 1]!

	// Idiom 1 — weekly weekday: w[d{…weekday…}] -> "every Monday", "every other Friday".
	if (frames.length === 2 && coarsest.unit === 'w' && finest.unit === 'd' && finest.parent === 'w') {
		return weeklyWeekday(coarsest, finest)
	}

	// Idiom 2 — anchored date: (y-all?) M{month} d{day} -> "the 5th of June".
	const date = tryDate(frames)
	if (date) return date

	// Idiom 3 — a lone day-of-month needs its anchor spelled out: d{5} -> "the 5th of the month".
	if (frames.length === 1 && isDayOfMonth(finest)) {
		return `${phraseFrame(finest)} of the month`
	}

	// General: render each frame and chain fine "of" coarse ("the 1st and 15th of every month").
	return frames.map(phraseFrame).reverse().join(' of ')
}

/** w[d{…}] shapes: "every Monday", "every other Friday", "Mon & Wed, every 3 weeks", "the 1st Wednesday". */
function weeklyWeekday(week: Frame, day: Frame): string {
	const days = phraseFrame(day)
	if (week.selection.kind === 'all') {
		const interval = week.interval && week.interval > 1 ? week.interval : 1
		if (interval === 1) return `every ${days}`
		if (interval === 2) return `every other ${days}`
		return `${days}, every ${interval} weeks`
	}
	const weekValue = onlyValue(week.selection)
	return weekValue ? `the ${ordinal(weekValue.value)} ${days}` : `${days} of ${phraseFrame(week)}`
}

/** (y-all?) + M{single named} + d{single} -> "the 5th of June"; null when the frames aren't that shape. */
function tryDate(frames: Frame[]): string | null {
	const day = frames[frames.length - 1]!
	if (!isDayOfMonth(day) || (day.interval && day.interval > 1)) return null
	const dayValue = onlyValue(day.selection)
	if (!dayValue) return null

	const month = frames[frames.length - 2]
	if (!month || month.unit !== 'M' || (month.interval && month.interval > 1)) return null
	const monthValue = onlyValue(month.selection)
	if (!monthValue?.name) return null

	// Anything coarser than the month must be a plain "every year" (annual, and implied by naming the month).
	const coarser = frames.slice(0, frames.length - 2)
	if (!coarser.every(frame => frame.unit === 'y' && frame.selection.kind === 'all' && !(frame.interval && frame.interval > 1))) {
		return null
	}
	return `the ${ordinal(dayValue.value)} of ${monthValue.name}`
}

/** One frame in isolation: "every month", "Monday and Wednesday", "the 1st and 15th", "3 random weeks". */
function phraseFrame(frame: Frame): string {
	const unit = UNIT_WORD[frame.unit]
	const interval = frame.interval && frame.interval > 1 ? frame.interval : undefined
	const sel = frame.selection
	const dayOfMonth = isDayOfMonth(frame)

	if (sel.kind === 'all') {
		return interval ? intervalWord(frame.unit, interval) : `every ${unit}`
	}
	if (sel.kind === 'random') {
		return `${sel.count} random ${unit}${sel.count > 1 ? 's' : ''}`
	}
	if (sel.kind === 'range') {
		const inner = valueRange(sel, unit, dayOfMonth)
		return interval ? `${intervalWord(frame.unit, interval)} from ${inner}` : inner
	}
	const inner = valueList(sel, unit, dayOfMonth)
	return interval ? `${intervalWord(frame.unit, interval)} from ${inner}` : inner
}

function valueList(sel: Extract<Selection, { kind: 'list' }>, unit: string, dayOfMonth: boolean): string {
	if (sel.values.every(value => !!value.name)) {
		return joinAnd(sel.values.map(value => value.name!))
	}
	const ordinals = joinAnd(sel.values.map(value => ordinal(value.value)))
	// Day-of-month reads bare ("the 5th"); other unnamed units keep their word ("the 2nd and 4th weeks").
	return dayOfMonth ? `the ${ordinals}` : `the ${ordinals} ${unit}${sel.values.length > 1 ? 's' : ''}`
}

function valueRange(sel: Extract<Selection, { kind: 'range' }>, unit: string, dayOfMonth: boolean): string {
	if (sel.start.name && sel.end.name) return `${sel.start.name} through ${sel.end.name}`
	const span = `${ordinal(sel.start.value)} through ${ordinal(sel.end.value)}`
	return dayOfMonth ? `the ${span}` : `the ${span} ${unit}s`
}

/** "every other week" for interval 2, "every 3 weeks" otherwise — the idiomatic reading of `%N`. */
function intervalWord(unit: CalendarUnit, interval: number): string {
	return interval === 2 ? `every other ${UNIT_WORD[unit]}` : `every ${interval} ${UNIT_WORD[unit]}s`
}

function phraseBound(bound: Bound): string {
	switch (bound.kind) {
		case 'count': return `up to ${bound.times} time${bound.times > 1 ? 's' : ''}`
		case 'perCycle': return `up to ${bound.times} time${bound.times > 1 ? 's' : ''} per cycle`
		case 'after': return `from ${bound.at}`
		case 'before': return `until ${bound.at}`
	}
}

/**
 * The time-of-day clause after the frames. A plain clock reads " at 09:00". A {@link ClockStep} reads as a cadence
 * instead: an hour step runs from each listed time to the end of the day (", every 2 hours from 09:00"); a listed
 * minute or second step runs to the end of its hour or minute (", every 15 minutes from 09:10 to the end of the
 * hour"); and a streaming minute or second stays within the coarser values (", every 15 minutes within the 09:00
 * hour", ", every 10 seconds within the 09:00 minute").
 */
function clockClause(time: ClockTime): string {
	const step = time.step
	if (!step) return ` at ${formatClock(time)}`

	const every = clockEvery(step)
	if (step.unit === 'h') return `, ${every} from ${formatClock(time)}`

	const window = step.unit === 'm' ? 'hour' : 'minute'
	const streaming = (step.unit === 'm' ? time.minutes : time.seconds).length === 0
	if (!streaming) {
		const windows = step.unit === 'm' ? time.hours.length : time.hours.length * time.minutes.length
		return `, ${every} from ${formatClock(time)} to the end of ${windows > 1 ? 'each' : 'the'} ${window}`
	}

	const windows = step.unit === 'm'
		? time.hours.map(hour => `${pad(hour)}:00`)
		: time.hours.flatMap(hour => time.minutes.map(minute => `${pad(hour)}:${pad(minute)}`))
	return `, ${every} within the ${joinAnd(windows)} ${window}${windows.length > 1 ? 's' : ''}`
}

/** "every hour", "every other minute", "every 15 minutes" — the cadence of a clock step. */
function clockEvery(step: ClockStep): string {
	const word = CLOCK_WORD[step.unit]
	if (step.interval <= 1) return `every ${word}`
	return step.interval === 2 ? `every other ${word}` : `every ${step.interval} ${word}s`
}

function formatClock(time: ClockTime): string {
	const parts: string[] = []
	// Streaming minutes leave only the hours to name.
	const minutes = time.minutes.length > 0 ? time.minutes : [0]
	for (const hour of time.hours) {
		for (const minute of minutes) {
			if (time.seconds.length > 0) {
				for (const second of time.seconds) parts.push(`${pad(hour)}:${pad(minute)}:${pad(second)}`)
			} else {
				parts.push(`${pad(hour)}:${pad(minute)}`)
			}
		}
	}
	return joinAnd(parts)
}

function formatDuration(parts: DurationPart[]): string {
	return joinAnd(parts.map(part => `${part.count} ${DUR_WORD[part.unit]}${part.count > 1 ? 's' : ''}`))
}

/** The single value of a one-element list or a degenerate range, or null when the selection has several. */
function onlyValue(selection: Selection): NamedValue | null {
	if (selection.kind === 'list' && selection.values.length === 1) return selection.values[0]!
	if (selection.kind === 'range' && selection.start.value === selection.end.value) return selection.start
	return null
}

/** A `d` frame that is a day-of-month (its parent is the month) — not a weekday, not a day-of-year. */
function isDayOfMonth(frame: Frame): boolean {
	return frame.unit === 'd' && frame.parent === 'M'
}

function joinAnd(list: string[]): string {
	if (list.length === 0) return ''
	if (list.length === 1) return list[0]!
	if (list.length === 2) return `${list[0]} and ${list[1]}`
	return `${list.slice(0, -1).join(', ')}, and ${list[list.length - 1]}`
}

function ordinal(n: number): string {
	const suffixes = ['th', 'st', 'nd', 'rd']
	const v = n % 100
	return n + (suffixes[(v - 20) % 10] ?? suffixes[v] ?? suffixes[0]!)
}

function pad(n: number): string {
	return n.toString().padStart(2, '0')
}
