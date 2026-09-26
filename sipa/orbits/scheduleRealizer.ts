// Stage 2: ScheduleModel -> phrase. "Realization" in NLG terms — turn meaning into words. This is the LONG
// register; a short/spoken register is the same walk with a different lexicon and ordering (a later step).
//
// Note what this file does NOT import: the calendar. Normalization already resolved weekday/month names into
// the model, so realization is pure meaning -> words. Bounds and durations, which the legacy humaniser
// dropped, are rendered here for free.
//
// The realizer recognises a few IDIOMS (peepholes over the frame list) for natural reading — a weekly
// weekday, a weekday of a month's week, an anchored calendar date, a day-of-month — and falls back to a
// generic "fine of coarse" chain for everything else. Limits read in the unit they count: a node's first
// values ("the first 3 days of every week"), the outermost frame's run ("for 10 weeks"), and an emission cap
// per period of the node's parent ("up to 7 times a month") or in all.

import type { DurationPart, SetOperator, TimeUnit } from './ast'
import type { Bound, CalendarUnit, ClockStep, ClockTime, CompoundSchedule, Frame, InstantSchedule, NamedValue, ScheduleModel, Selection, SimpleSchedule } from './scheduleModel'

const UNIT_WORD: Record<CalendarUnit, string> = { y: 'year', M: 'month', w: 'week', d: 'day' }
const CLOCK_WORD: Record<ClockStep['unit'], string> = { h: 'hour', m: 'minute', s: 'second' }
const DUR_WORD: Record<DurationPart['unit'], string> = { y: 'year', M: 'month', w: 'week', d: 'day', h: 'hour', m: 'minute', s: 'second' }
// "a month", "an hour": the period an emission cap counts in.
const PER_WORD: Record<TimeUnit, string> = { y: 'a year', M: 'a month', w: 'a week', d: 'a day', h: 'an hour', m: 'a minute', s: 'a second' }

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

	const clauses: string[] = []
	const outer = model.frames[0]
	if (outer?.repeat !== undefined) clauses.push(outerRun(outer, model))
	clauses.push(...model.bounds.map(phraseBound))
	if (clauses.length > 0) text += `, ${clauses.join(', ')}`
	return text
}

/**
 * The outermost frame's `*x`: the schedule runs x of its periods ("for 10 weeks"). Stepped by `%N`, x periods would
 * read as a span of x units, so a single-occurrence schedule says how many times ("3 times") and any other how many of
 * its periods ("for 3 such weeks").
 */
function outerRun(outer: Frame, model: SimpleSchedule): string {
	const count = outer.repeat!
	const unit = UNIT_WORD[outer.unit]
	if ((outer.interval ?? 1) <= 1) return `for ${count} ${unit}${count > 1 ? 's' : ''}`
	const single = model.frames.length === 1 && !model.time
	if (single) return timesWord(count)
	return count === 1 ? `for one such ${unit}` : `for ${count} such ${unit}s`
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
	const outer = model.frames[0]
	if (outer?.repeat !== undefined) parts.push(shortOuterRun(outer, model))
	for (const bound of model.bounds) parts.push(shortBound(bound))
	return parts.join(' ')
}

/** The outermost frame's run, terse: "×10w" for ten weeks, "×3" for three single occurrences stepped by `%N`. */
function shortOuterRun(outer: Frame, model: SimpleSchedule): string {
	const single = model.frames.length === 1 && !model.time
	return (outer.interval ?? 1) > 1 && single ? `×${outer.repeat}` : `×${outer.repeat}${SHORT_DUR[outer.unit]}`
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
	// The outermost frame's own run follows the frames ("×10w"), so it reads plain here.
	frames = withoutOuterRun(frames)
	const monthly = shortMonthlyWeekday(frames)
	if (monthly) return monthly

	const kept = frames.filter((frame, index) => !isRedundantParent(frame, frames[index + 1]))
	const coarsest = kept[0]!
	const finest = kept[kept.length - 1]!
	if (kept.length === 2 && isWeeklyWeekday(coarsest, finest)) {
		return shortWeekly(coarsest, finest)
	}
	const date = shortDate(kept)
	if (date) return date

	// A single selected frame over plain cadence: render just that selection. A unit-prefixed value ("w1") or
	// a day-of-month ("1st") stands alone, but a day-of-year ("d1") would be misread as a day-of-month, so it
	// takes a "/cadence" tag to disambiguate ("d1 /y").
	const selected = kept.filter(frame => frame.selection.kind !== 'all' || (frame.interval ?? 1) > 1)
	if (selected.length === 1) {
		const only = selected[0]!
		const payload = shortFrame(only)
		if (only.unit === 'd' && !isDayOfMonth(only) && coarsest.selection.kind === 'all' && coarsest !== only) {
			return `${payload} /${SHORT_DUR[coarsest.unit]}`
		}
		return payload
	}

	return kept
		.map((frame, index) => shortFrame(frame, index > 0))
		.filter(part => part.length > 0)
		.join(' ')
}

function shortWeekly(week: Frame, day: Frame): string {
	const days = day.selection.kind === 'all' ? 'daily' : shortSelection(day)
	if (week.selection.kind === 'all') {
		const interval = week.interval && week.interval > 1 ? week.interval : 1
		return interval === 1 ? days : `${days} /${interval}w`
	}
	return `${shortWeekValues(week)} ${days}`
}

/** A weekday of a month's week, terse: "1st Mon", "2nd&4th Fri", "1st-2nd Mon", or the weekdays of each week. */
function shortMonthlyWeekday(frames: Frame[]): string | null {
	const shape = monthlyWeekdayShape(frames)
	if (!shape) return null
	const { week, day } = shape
	if (week.selection.kind === 'all') return shortSelection(day)
	return `${shortWeekValues(week)} ${shortSelection(day)}`
}

/** The weeks a week frame names, as ordinals: "1st", "2nd&4th", "1st-2nd". */
function shortWeekValues(week: Frame): string {
	const sel = week.selection
	if (sel.kind === 'list') return sel.values.map(value => ordinal(value.value)).join('&')
	if (sel.kind === 'range' || sel.kind === 'first') {
		return sel.start.value === sel.end.value ? ordinal(sel.start.value) : `${ordinal(sel.start.value)}-${ordinal(sel.end.value)}`
	}
	return shortSelection(week)
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

function shortFrame(frame: Frame, inner = false): string {
	const interval = frame.interval && frame.interval > 1 ? frame.interval : undefined
	if (frame.selection.kind === 'all') {
		const cadence = interval ? `/${interval}${SHORT_DUR[frame.unit]}` : SHORT_CADENCE[frame.unit]
		// An inner frame's `*x` under a continuous `%N`: the first x of its on-phase values.
		return inner && frame.repeat !== undefined ? `${cadence} first${frame.repeat}` : cadence
	}
	const body = shortSelection(frame)
	return interval ? `${body} /${interval}${SHORT_DUR[frame.unit]}` : body
}

/**
 * A frame's values, compactly: clipped names (Mon, Jun); ordinals for a day-of-month (1st); otherwise the value
 * carries its unit letter so it is not a bare, meaningless number ("w1", "y2027", "d1" for a day-of-year). The first
 * few values of a unit read as the run they are ("Mon-Wed", "1st-10th").
 */
function shortSelection(frame: Frame): string {
	const sel = frame.selection
	const dayOfMonth = isDayOfMonth(frame)
	const prefix = SHORT_DUR[frame.unit]
	if (sel.kind === 'all') return SHORT_CADENCE[frame.unit]
	if (sel.kind === 'random') return `${sel.count}?${prefix}`
	if (sel.kind === 'range' || sel.kind === 'first') {
		if (sel.start.name && sel.end.name) return sel.start.value === sel.end.value ? shortLabel(sel.start) : `${shortLabel(sel.start)}-${shortLabel(sel.end)}`
		if (sel.start.value === sel.end.value) return dayOfMonth ? ordinal(sel.start.value) : `${prefix}${sel.start.value}`
		return dayOfMonth ? `${ordinal(sel.start.value)}-${ordinal(sel.end.value)}` : `${prefix}${sel.start.value}-${sel.end.value}`
	}
	return sel.values.map(value =>
		value.name ? shortLabel(value) : (dayOfMonth ? ordinal(value.value) : `${prefix}${value.value}`)
	).join('&')
}

/** The clock, terse: "@9", and a step after it as its cadence ("@9 /2h", "@9 /15m" within the 9 o'clock hour). */
function shortClockParts(time: ClockTime): string[] {
	const clock = `@${shortClock(time)}`
	if (!time.step) return [clock]
	const cadence = `/${time.step.interval}${SHORT_DUR[time.step.unit]}`
	return time.step.times === undefined ? [clock, cadence] : [clock, cadence, `×${time.step.times}/${SHORT_DUR[coarserClock(time.step.unit)]}`]
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
		case 'count': return bound.per ? `×${bound.times}/${SHORT_DUR[bound.per]}` : `×${bound.times}`
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

	// The outermost frame's own run is voiced after the phrase ("for 10 weeks"), so it reads plain here.
	frames = withoutOuterRun(frames)

	// Idiom 1 — a weekday of a month's weeks: M[w{1}[d{1}]] -> "the 1st Monday of every month". The month numbers the
	// weeks, so this reads before any plain parent is left out.
	const monthly = monthlyWeekday(frames)
	if (monthly) return monthly

	// A plain parent that adds nothing is left out, and the rest reads without it.
	const kept = frames.filter((frame, index) => !isRedundantParent(frame, frames[index + 1]))
	const coarsest = kept[0]!
	const finest = kept[kept.length - 1]!

	// Idiom 2 — weekly weekday: w[d{…weekday…}] -> "every Monday", "every other Friday", "the first 3 days of every week".
	if (kept.length === 2 && isWeeklyWeekday(coarsest, finest)) {
		return weeklyWeekday(coarsest, finest)
	}

	// Idiom 3 — anchored date: M{month} d{day} -> "the 5th of June".
	const date = tryDate(kept)
	if (date) return date

	// Idiom 4 — a lone day-of-month selection needs its anchor spelled out: d{5} -> "the 5th of the month".
	if (kept.length === 1 && isDayOfMonth(finest) && finest.selection.kind !== 'all') {
		return `${phraseFrame(finest)} of the month`
	}

	// General: render each frame and chain fine "of" coarse ("the 1st and 15th of every month"). Named months left on
	// their own recur yearly: "every June".
	const phrases = kept.map(phraseFrame)
	if (kept.length === 1 && kept[0] !== frames[0] && finest.unit === 'M' && namedDays(finest.selection)) phrases[0] = `every ${phrases[0]}`
	return phrases.reverse().join(' of ')
}

/**
 * Whether a plain frame says nothing its child does not: above a frame that takes every value of its unit ("every day
 * of every month" is every day), or a plain year above named months (naming June already says "every year").
 */
function isRedundantParent(frame: Frame, child: Frame | undefined): boolean {
	if (!child || !isPlain(frame)) return false
	if (isBare(child)) return true
	return frame.unit === 'y' && child.unit === 'M' && !(child.interval && child.interval > 1) && namedDays(child.selection)
}

/** The frames with the outermost one's own run (`*x`) set aside, for the phrase that reads the run separately. */
function withoutOuterRun(frames: Frame[]): Frame[] {
	const [outer, ...inner] = frames
	return outer?.repeat === undefined ? frames : [{ ...outer, repeat: undefined }, ...inner]
}

/** A frame that selects every value of its unit, with nothing to say beyond "every <unit>". */
function isPlain(frame: Frame): boolean {
	return isBare(frame) && !(frame.interval && frame.interval > 1)
}

/** A frame that takes every value of its unit, or every N-th under a continuous `%N`: its parent does not shape it. */
function isBare(frame: Frame): boolean {
	return frame.selection.kind === 'all' && frame.repeat === undefined
}

/** w[d] shapes the weekly idiom voices: a bare or single-valued week over a weekday selection. */
function isWeeklyWeekday(week: Frame, day: Frame): boolean {
	return week.unit === 'w' && day.unit === 'd' && day.parent === 'w' && !(day.interval && day.interval > 1) && day.repeat === undefined
		&& (week.selection.kind === 'all' || onlyValue(week.selection) !== null)
}

/** w[d{…}] shapes: "every Monday", "every other Friday", "Mon & Wed, every 3 weeks", "the 1st Wednesday of every month". */
function weeklyWeekday(week: Frame, day: Frame): string {
	const sel = day.selection
	if (week.selection.kind !== 'all') {
		const weekValue = onlyValue(week.selection)!
		return `the ${ordinal(weekValue.value)} ${phraseFrame(day)} of every month`
	}
	const interval = week.interval && week.interval > 1 ? week.interval : 1
	const every = interval === 1 ? 'every week' : interval === 2 ? 'every other week' : `every ${interval} weeks`
	// Selections that are not named days read as a share of the week.
	if (sel.kind === 'all') return interval === 1 ? 'every day' : `every day of ${every}`
	if (sel.kind === 'random' || sel.kind === 'first') return `${phraseFrame(day)} of ${every}`
	const days = phraseFrame(day)
	if (interval === 1) return `every ${days}`
	if (interval === 2) return `every other ${days}`
	return `${days}, every ${interval} weeks`
}

/**
 * The weekday frames of a month's weeks, when the frames are M[w[d]] with a plain month and a weekday selection over
 * named days. Null for any other shape.
 */
function monthlyWeekdayShape(frames: Frame[]): { month: Frame; week: Frame; day: Frame } | null {
	if (frames.length !== 3) return null
	const [month, week, day] = frames as [Frame, Frame, Frame]
	if (month.unit !== 'M' || week.unit !== 'w' || day.unit !== 'd' || day.parent !== 'w') return null
	if (!isPlain(month)) return null
	if (week.interval && week.interval > 1) return null
	if (day.interval && day.interval > 1) return null
	if (day.selection.kind !== 'list' && day.selection.kind !== 'range') return null
	if (!namedDays(day.selection)) return null
	return { month, week, day }
}

/**
 * M[w[d{weekday}]] shapes. A week is counted within the month from its first complete one, so a week index is the
 * n-th occurrence of the weekday in the month: "the 1st Monday of every month", "the 2nd and 4th Friday of every
 * month", "the first 2 Mondays of every month". A bare week over several weekdays reads as the weekdays of each
 * week, which a month starts counting at its first complete week — that is, from the month's first occurrence of
 * the earliest of them: "every Monday, Wednesday, and Friday from each month's first Monday".
 */
function monthlyWeekday(frames: Frame[]): string | null {
	const shape = monthlyWeekdayShape(frames)
	if (!shape) return null
	const { week, day } = shape
	const single = onlyValue(day.selection)
	const days = phraseFrame(day)

	if (week.selection.kind === 'all') {
		if (single) return `every ${days}`
		const first = day.selection.kind === 'list' ? day.selection.values.reduce((a, b) => (b.value < a.value ? b : a)) : (day.selection as Extract<Selection, { kind: 'range' }>).start
		return `every ${days} from each month's first ${first.name}`
	}
	if (!single) return `${days} of ${phraseFrame(week)} of every month`
	const plural = `${single.name}s`
	switch (week.selection.kind) {
		case 'first':
			return week.selection.count === 1 ? `the first ${single.name} of every month` : `the first ${week.selection.count} ${plural} of every month`
		case 'list':
			return `the ${joinAnd(week.selection.values.map(value => ordinal(value.value)))} ${week.selection.values.length > 1 ? plural : single.name} of every month`
		case 'range':
			return `the ${ordinal(week.selection.start.value)} through ${ordinal(week.selection.end.value)} ${plural} of every month`
		case 'random':
			return `${single.name} of ${week.selection.count} random weeks of every month`
	}
}

/** Whether every value a list or range selection names has a name (a weekday selection, say). */
function namedDays(selection: Selection): boolean {
	if (selection.kind === 'list') return selection.values.every(value => !!value.name)
	if (selection.kind === 'range') return !!selection.start.name && !!selection.end.name
	return false
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

/** One frame in isolation: "every month", "Monday and Wednesday", "the 1st and 15th", "3 random weeks", "the first 3 days". */
function phraseFrame(frame: Frame): string {
	const unit = UNIT_WORD[frame.unit]
	const interval = frame.interval && frame.interval > 1 ? frame.interval : undefined
	const sel = frame.selection
	const dayOfMonth = isDayOfMonth(frame)

	if (sel.kind === 'all') {
		const every = interval ? intervalWord(frame.unit, interval) : `every ${unit}`
		// An inner frame's `*x` under a continuous `%N`: the first x of its on-phase values in each parent period.
		return frame.repeat !== undefined ? `the first ${frame.repeat} of ${every}` : every
	}
	if (sel.kind === 'random') {
		return `${sel.count} random ${unit}${sel.count > 1 ? 's' : ''}`
	}
	if (sel.kind === 'first') {
		return sel.count === 1 ? `the first ${unit}` : `the first ${sel.count} ${unit}s`
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
		case 'count': return bound.per ? `${upTo(bound.times)} ${PER_WORD[bound.per]}` : inAll(bound.times)
		case 'after': return `from ${bound.at}`
		case 'before': return `until ${bound.at}`
	}
}

/** An emission cap within a period: "at most once", "up to twice", "up to 7 times". */
function upTo(times: number): string {
	return times === 1 ? 'at most once' : `up to ${timesWord(times)}`
}

/** An emission cap over the whole schedule: "only once", "twice in all", "10 times in all". */
function inAll(times: number): string {
	return times === 1 ? 'only once' : `${timesWord(times)} in all`
}

function timesWord(times: number): string {
	return times === 1 ? 'once' : times === 2 ? 'twice' : `${times} times`
}

/**
 * The time-of-day clause after the frames. A plain clock reads " at 09:00". A {@link ClockStep} reads as a cadence
 * instead: an hour step runs from each listed time to the end of the day (", every 2 hours from 09:00"); a listed
 * minute or second step runs to the end of its hour or minute (", every 15 minutes from 09:10 to the end of the
 * hour"); and a streaming minute or second stays within the coarser values (", every 15 minutes within the 09:00
 * hour", ", every 10 seconds within the 09:00 minute") — or, when it keeps only its first few, runs out sooner
 * (", every minute from 09:00 to 09:29", ", every 15 minutes within the 09:00 hour, up to 2 times an hour").
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
	if (step.times !== undefined && step.interval <= 1 && windows.length === 1) {
		// A plain streaming run that keeps its first x values ends x-1 steps into its window.
		const [hour, minute] = windows[0]!.split(':').map(Number) as [number, number]
		const last = step.unit === 'm' ? `${pad(hour)}:${pad(step.times - 1)}` : `${pad(hour)}:${pad(minute)}:${pad(step.times - 1)}`
		const first = step.unit === 'm' ? `${pad(hour)}:00` : `${pad(hour)}:${pad(minute)}:00`
		return `, ${every} from ${first} to ${last}`
	}
	const within = `, ${every} within the ${joinAnd(windows)} ${window}${windows.length > 1 ? 's' : ''}`
	return step.times === undefined ? within : `${within}, ${upTo(step.times)} ${PER_WORD[coarserClock(step.unit)]}`
}

/** The clock unit a finer one streams within: minutes within the hour, seconds within the minute. */
function coarserClock(unit: ClockStep['unit']): TimeUnit {
	return unit === 's' ? 'm' : unit === 'm' ? 'h' : 'd'
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
