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
import type { Bound, CalendarUnit, ClockTime, CompoundSchedule, Frame, NamedValue, ScheduleModel, Selection, SimpleSchedule } from './scheduleModel'

const UNIT_WORD: Record<CalendarUnit, string> = { y: 'year', M: 'month', w: 'week', d: 'day' }
const DUR_WORD: Record<DurationPart['unit'], string> = { y: 'year', M: 'month', w: 'week', d: 'day', h: 'hour', m: 'minute', s: 'second' }

// Long-register set connectives, reading as "<schedule> <connector> <schedule>".
const SET_CONNECTOR: Record<SetOperator, string> = {
	'+': ' and ',                      // union: both schedules fire
	'&': ' that also fall on ',        // intersection: only when they coincide
	'-': ', except ',                  // difference: A minus B
	'^': ' or, but never both, ',      // symmetric difference
}

/** Renders a schedule's meaning as a full English phrase (long register). */
export function realizeLong(model: ScheduleModel): string {
	return model.kind === 'compound' ? realizeCompound(model) : realizeSimple(model)
}

function realizeCompound(model: CompoundSchedule): string {
	return `${realizeLong(model.left)}${SET_CONNECTOR[model.operator]}${realizeLong(model.right)}`
}

function realizeSimple(model: SimpleSchedule): string {
	let text = phraseFrames(model.frames)
	if (model.time) text += ` at ${formatClock(model.time)}`
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
		return `${realizeShort(model.left)} ${model.operator} ${realizeShort(model.right)}`
	}

	const parts: string[] = []
	const base = shortFrames(model.frames)
	if (base) parts.push(base)
	if (model.time) parts.push(`@${shortClock(model.time)}`)
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
	return `${clip(monthValue.name)} ${dayValue.value}`
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
		if (sel.start.name && sel.end.name) return `${clip(sel.start.name)}-${clip(sel.end.name)}`
		return dayOfMonth ? `${ordinal(sel.start.value)}-${ordinal(sel.end.value)}` : `${prefix}${sel.start.value}-${sel.end.value}`
	}
	return sel.values.map(value =>
		value.name ? clip(value.name) : (dayOfMonth ? ordinal(value.value) : `${prefix}${value.value}`)
	).join('&')
}

function shortClock(time: ClockTime): string {
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

function formatClock(time: ClockTime): string {
	const parts: string[] = []
	for (const hour of time.hours) {
		for (const minute of time.minutes) {
			parts.push(time.seconds.length > 0
				? time.seconds.map(second => `${pad(hour)}:${pad(minute)}:${pad(second)}`).join(', ')
				: `${pad(hour)}:${pad(minute)}`)
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
