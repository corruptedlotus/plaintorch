// The "meaning" of an Orbit notation — the NLG content model (stage 1 of parse → normalize → realize).
//
// This is deliberately NOT the parse tree (that's ast.ts). The parse tree records HOW a schedule was
// written; this records WHAT it means, in a notation-independent way. `z{12:00}` and `h{12}[m{0}]` parse
// to different ASTs but normalize to the *same* ScheduleModel — because they mean the same thing. Once
// meaning lives here, "how to say it" (scheduleRealizer.ts) and "in which register" become separate,
// swappable concerns, and fields the old humaniser silently dropped (limits, durations) are just data.
//
// The model humaniser (this file + scheduleNormalizer/scheduleRealizer) was built here and has since
// graduated upstream to @pleiades/orbits; these three files are now kept in sync with it. Unlike the
// older vendored files they are strict-clean, so they carry no @ts-nocheck. scheduleDescribe.ts and
// pleiadeanNaming.ts stay plugin-only (the preview tool's calendar resolution and naming shim).

import type { DurationPart, SetOperator, TimeUnit } from './ast'

/** Calendar units only (y/M/w/d) — the "which day/week/month" units. h/m/s are handled as a clock time. */
export type CalendarUnit = 'y' | 'M' | 'w' | 'd'

/**
 * A schedule is either one plain recurrence, or a set-combination of two schedules. Most real notations
 * are `simple`; `compound` is the honest representation of genuine set-algebra (`+ & ^ -`) that does not
 * collapse into a single clause, so the realizer can still say *something* true about it.
 */
export type ScheduleModel = SimpleSchedule | CompoundSchedule | InstantSchedule

export interface CompoundSchedule {
	kind: 'compound'
	operator: SetOperator
	left: ScheduleModel
	right: ScheduleModel
}

/**
 * A single fixed moment, from the `Z{y/M/d[Th:m[:s]]}` literal — not a recurrence but one exact
 * calendar datetime ("5 June 2027 at 18:00"). Its own kind because a pinned date reads as a date,
 * not as the "which day of which month of which year" chain that its expansion would otherwise produce.
 */
export interface InstantSchedule {
	kind: 'instant'
	year: number
	/** The month, carrying the calendar's name (and short name) for it. */
	month: NamedValue
	day: number
	/** The time of day, when the literal pinned one (its `T` part). */
	time?: ClockTime
	/** How long the moment lasts, from `=<dur>`. Undefined = a point in time. */
	span?: DurationPart[]
	/** Bounds from `*x @x <t >t` — rare on a fixed instant, but modelled rather than dropped. */
	bounds: Bound[]
}

export interface SimpleSchedule {
	kind: 'simple'
	/**
	 * The addressed calendar units, coarse → fine (e.g. week, then day-of-week). The coarsest frame is the
	 * recurrence base ("every week…"); finer frames narrow it ("…on Monday"). Empty means the schedule is
	 * only a time of day ("every day at 09:00").
	 */
	frames: Frame[]
	/** The time of day from an h/m/s tail, if any. */
	time?: ClockTime
	/** How long each occurrence lasts, from `=<dur>`. Undefined = a point in time. */
	span?: DurationPart[]
	/** Bounds that cap or window the stream, from `*x @x <t >t`. */
	bounds: Bound[]
}

/** One addressed calendar unit: which values of it, how often, and what it sits inside (for naming/phrasing). */
export interface Frame {
	unit: CalendarUnit
	/** The enclosing unit — an actual parent when nested, else the unit's natural parent. Drives naming. */
	parent?: CalendarUnit
	/** `%N`: recur every N of this unit. Undefined/1 = every one. */
	interval?: number
	/** Which values within the parent this frame selects. */
	selection: Selection
}

/** Which occurrences of a unit are chosen. `all` = the bare unit with no `{}`. */
export type Selection =
	| { kind: 'all' }
	| { kind: 'list'; values: NamedValue[] }
	| { kind: 'range'; start: NamedValue; end: NamedValue }
	| { kind: 'random'; count: number }

/** A numeric index plus the calendar's name for it when it has one (weekday, month); else name is absent. */
export interface NamedValue {
	value: number
	/** The full name ("Monday", "June"), when the calendar names this value. */
	name?: string
	/** The calendar's own short name ("Mon", "Tva"), when it curates one — else the realizer clips {@link name}. */
	shortName?: string
}

/** A time of day. Kept as raw numbers so the realizer decides the format ("09:00", "9am", "@9"). */
export interface ClockTime {
	hours: number[]
	minutes: number[]
	seconds: number[]
}

/** A bound on the stream. */
export type Bound =
	| { kind: 'count'; times: number }       // @x — at most x occurrences in total
	| { kind: 'perCycle'; times: number }    // *x — at most x per enclosing cycle
	| { kind: 'after'; at: string }          // >t — not before this timestamp
	| { kind: 'before'; at: string }         // <t — not after this timestamp

/** The natural enclosing unit of a calendar unit when it is not explicitly nested under another. */
export function naturalParent(unit: CalendarUnit): CalendarUnit | undefined {
	switch (unit) {
		case 'y': return undefined
		case 'M': return 'y'
		case 'w': return 'M'
		case 'd': return 'M' // day-of-month by default; a `d` nested under `w` overrides this to weekday
	}
}

/** Whether a unit is a clock unit (folded into {@link ClockTime}) rather than a calendar frame. */
export function isClockUnit(unit: TimeUnit): unit is 'h' | 'm' | 's' {
	return unit === 'h' || unit === 'm' || unit === 's'
}
