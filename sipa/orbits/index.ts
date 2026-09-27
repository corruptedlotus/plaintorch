// Plugin-facing surface over the vendored @pleiades/orbits stack. The parser,
// humanizer and calendar are kept verbatim from orbit-scheduler; the model-based
// humaniser (scheduleModel/normalizer/realizer/describe) and the short humaniser
// are the plugin-authored layer on top.

export * from './ast'
export * from './calendar'
export * from './parser'
export * from './humanizer'
export * from './shortHumanizer'
export * from './scheduleModel'
export * from './scheduleNormalizer'
export * from './scheduleRealizer'
export * from './scheduleDescribe'
export * from './pleiadeanNaming'

import type { ASTNode } from './ast'
import { CalendarSystem, GregorianCalendar } from './calendar'
import { OrbitHumanizer } from './humanizer'
import { OrbitParser } from './parser'
import { OrbitShortHumanizer } from './shortHumanizer'
import { PleiadeanNamingCalendar } from './pleiadeanNaming'
import { normalizeSchedule } from './scheduleNormalizer'
import { realizeLong, realizeShort } from './scheduleRealizer'

/**
 * The calendar an orbit is read on, by name: the one its entity resolves against (an explicit per-entity calendar,
 * else the vault's preferred one). It decides the names the reading uses — "June" or "Tārvan", and which weekday
 * a week's first day is — so a humanised orbit says what the core will resolve.
 */
export type OrbitCalendarName = 'gregorian' | 'pleiadean'

// The humanizers and calendars are stateless, so one shared instance of each, per calendar, is enough.
const calendars: Record<OrbitCalendarName, CalendarSystem> = {
	gregorian: new GregorianCalendar(),
	pleiadean: new PleiadeanNamingCalendar(),
}

const humanizers = new Map<CalendarSystem, { long: OrbitHumanizer, short: OrbitShortHumanizer }>()

/** The shared calendar for a name, or the given calendar itself. Gregorian when none is named. */
export function orbitCalendar(calendar: OrbitCalendarName | CalendarSystem = 'gregorian'): CalendarSystem {
	return typeof calendar === 'string' ? calendars[calendar] : calendar
}

/** The fallback humanisers for a calendar, made once and kept. */
function humanizersFor(calendar: CalendarSystem) {
	let pair = humanizers.get(calendar)
	if (!pair) {
		pair = { long: new OrbitHumanizer(calendar), short: new OrbitShortHumanizer(calendar) }
		humanizers.set(calendar, pair)
	}
	return pair
}

/** Result of turning a raw Orbit notation into a human-readable phrase. */
export interface HumanizedOrbit {
	/** The human-readable phrase (e.g. "every Monday at 09:00"), or the raw notation on parse failure. */
	text: string
	/** True when the notation could not be parsed and {@link text} is the raw fallback. */
	invalid: boolean
}

/**
 * Turns a raw Orbit notation into a human-readable phrase.
 *
 * The reading comes from the model-based humaniser (parse → normalize → realize): the notation is
 * understood as a {@link ScheduleModel}, then voiced in the requested register — the full one
 * ("Every other Friday at 17:30") or, with `short`, the compact one ("Fri /2w @17:30"). For a shape
 * the model does not cover yet, it falls back to the vendored long humaniser (and, for the short
 * register, the legacy short humaniser first), so exotic notation is rendered plainly rather than crash.
 *
 * Returns the raw notation (flagged invalid) when it cannot be parsed at all, so callers can still show
 * something meaningful while a user is mid-edit.
 *
 * The reading names months and weekdays on `calendar` — the calendar the orbit's entity resolves against. It
 * defaults to Gregorian, so a surface showing an entity's orbit must pass that entity's calendar.
 */
export function humanizeOrbit(orbit: string | undefined | null, short = false, calendar?: OrbitCalendarName | CalendarSystem): HumanizedOrbit {
	const raw = orbit?.trim() ?? ''
	if (!raw) {
		return { text: '', invalid: false }
	}

	try {
		const ast = new OrbitParser(raw).parse()
		const system = orbitCalendar(calendar)
		const phrase = short ? shortPhrase(ast, system) : longPhrase(ast, system)
		return { text: capitalizeFirst(phrase), invalid: false }
	} catch {
		return { text: raw, invalid: true }
	}
}

/** The full reading: the model realizer when it covers the shape, else the vendored humaniser. */
function longPhrase(ast: ASTNode, calendar: CalendarSystem): string {
	try {
		return realizeLong(normalizeSchedule(ast, calendar))
	} catch {
		return humanizersFor(calendar).long.serialize(ast)
	}
}

/** The terse reading: the model realizer, then the legacy short humaniser, then the vendored long one. */
function shortPhrase(ast: ASTNode, calendar: CalendarSystem): string {
	try {
		return realizeShort(normalizeSchedule(ast, calendar))
	} catch {
		try {
			return humanizersFor(calendar).short.serialize(ast)
		} catch {
			return humanizersFor(calendar).long.serialize(ast)
		}
	}
}

/** Whether a raw Orbit notation parses successfully. */
export function isValidOrbit(orbit: string | undefined | null): boolean {
	const raw = orbit?.trim() ?? ''
	if (!raw) return false
	try {
		new OrbitParser(raw).parse()
		return true
	} catch {
		return false
	}
}

function capitalizeFirst(text: string): string {
	return text.length === 0 ? text : text.charAt(0).toUpperCase() + text.slice(1)
}
