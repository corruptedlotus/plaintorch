import { DeclarativeCalendar, OrbitUnit, PleiadeanDate } from "@pleiades/sdk"

/**
 * The arithmetic behind a moment read at a **granularity** (PEP111): an occurrence at hour granularity stands for its
 * whole hour, at month granularity for its whole month. Kept free of any element so it is exercised on its own.
 *
 * A moment arrives as a local wall-clock `Date` — the way a stored civil datetime is read — and every window is taken
 * on its wall-clock reading, so a time zone or daylight-saving shift never moves a boundary. Weeks, months and years
 * are the given calendar's, as the core resolves them: a Gregorian week starts on Monday and a Pleiadean one on
 * Saturday, a Pleiadean month runs 60 or 61 days, and a Pleiadean year is six of them.
 */

const dayMs = 86_400_000
const unitMs: Partial<Record<OrbitUnit, number>> = {
	[OrbitUnit.Second]: 1_000,
	[OrbitUnit.Minute]: 60_000,
	[OrbitUnit.Hour]: 3_600_000,
}

/** A half-open run of civil days, `[start, end)`, counted as days since 1970-01-01. */
export interface CivilWindow {
	start: number
	end: number
}

/** Whether a granularity is a calendar unit (week, month, year) — the ones whose windows depend on the calendar. */
export function isCalendarGranularity(granularity: OrbitUnit | undefined): boolean {
	return granularity === OrbitUnit.Week || granularity === OrbitUnit.Month || granularity === OrbitUnit.Year
}

/** Whether a granularity is finer than a day — the ones that carry a time of day. */
export function isClockGranularity(granularity: OrbitUnit | undefined): boolean {
	return granularity === OrbitUnit.Hour || granularity === OrbitUnit.Minute || granularity === OrbitUnit.Second
}

/** A local instant's wall-clock reading as a UTC epoch, so unit arithmetic sees neither the zone nor its shifts. */
function wallClock(at: Date): number {
	return Date.UTC(at.getFullYear(), at.getMonth(), at.getDate(), at.getHours(), at.getMinutes(), at.getSeconds())
}

/** The civil day a local instant falls on, as days since 1970-01-01. */
export function civilDay(at: Date): number {
	return Math.floor(wallClock(at) / dayMs)
}

/** A civil day as the UTC-midnight `Date` the Pleiadean calculator and UTC-zoned formatting read it as. */
export function civilDayDate(day: number): Date {
	return new Date(day * dayMs)
}

function modulo(value: number, divisor: number): number {
	return ((value % divisor) + divisor) % divisor
}

/** The week holding a civil day: from its Monday (Gregorian) or its Saturday (Pleiadean), seven days long. */
export function weekWindow(day: number, calendar: DeclarativeCalendar): CivilWindow {
	// 1970-01-01 was a Thursday, three days after a Monday and five after a Saturday.
	const start = day - modulo(day + (calendar === DeclarativeCalendar.Gregorian ? 3 : 5), 7)
	return { start, end: start + 7 }
}

/** The month holding a civil day, on the calendar, with an index that counts months across years. */
export function monthWindow(day: number, calendar: DeclarativeCalendar): CivilWindow & { index: number } {
	if (calendar === DeclarativeCalendar.Gregorian) {
		const date = civilDayDate(day)
		const year = date.getUTCFullYear()
		const month = date.getUTCMonth()
		return { index: year * 12 + month, start: Date.UTC(year, month, 1) / dayMs, end: Date.UTC(year, month + 1, 1) / dayMs }
	}

	const date = PleiadeanDate.fromDate(civilDayDate(day))
	const start = new PleiadeanDate(date.year, date.month, 1).toDate().getTime() / dayMs
	return { index: date.year * 6 + date.month - 1, start, end: start + PleiadeanDate.getDaysInMonth(date.year, date.month) }
}

/** The year holding a civil day, on the calendar. */
export function yearWindow(day: number, calendar: DeclarativeCalendar): CivilWindow & { index: number } {
	if (calendar === DeclarativeCalendar.Gregorian) {
		const year = civilDayDate(day).getUTCFullYear()
		return { index: year, start: Date.UTC(year, 0, 1) / dayMs, end: Date.UTC(year + 1, 0, 1) / dayMs }
	}

	const { year } = PleiadeanDate.fromDate(civilDayDate(day))
	const start = new PleiadeanDate(year, 1, 1).toDate().getTime() / dayMs
	return { index: year, start, end: start + PleiadeanDate.getDaysInYear(year) }
}

/**
 * Which window of a granularity a moment falls in, as a number that counts windows: two moments in the same hour
 * (for an hour) or the same month (for a month) share it, and consecutive windows differ by one.
 */
export function windowIndex(at: Date, granularity: OrbitUnit, calendar: DeclarativeCalendar): number {
	const span = unitMs[granularity]
	if (span !== undefined) {
		return Math.floor(wallClock(at) / span)
	}

	const day = civilDay(at)
	switch (granularity) {
		case OrbitUnit.Week:
			// Week starts are seven days apart, so flooring keeps the index a whole number that steps by exactly one.
			return Math.floor(weekWindow(day, calendar).start / 7)
		case OrbitUnit.Month:
			return monthWindow(day, calendar).index
		case OrbitUnit.Year:
			return yearWindow(day, calendar).index
		default:
			return day
	}
}

/**
 * Where `now` stands against a grained moment: still `ahead` of its window, `inside` it, or `past` it. Inside the
 * window a moment is neither overdue nor still to come — an hour-grained 18:00 is "this hour" at 18:36.
 */
export function grainedPhase(at: Date, granularity: OrbitUnit, calendar: DeclarativeCalendar, now: Date): 'ahead' | 'inside' | 'past' {
	const delta = windowIndex(at, granularity, calendar) - windowIndex(now, granularity, calendar)
	return delta > 0 ? 'ahead' : delta < 0 ? 'past' : 'inside'
}

function pad2(value: number): string {
	return String(value).padStart(2, '0')
}

/**
 * An hour on its own, the way the locale reads a clock: "9" and "13" on a 24-hour clock, "9AM" and "1PM" where the
 * locale marks the half of the day.
 */
export function hourFace(hour: number, locale?: string): string {
	const format = new Intl.DateTimeFormat(locale, { hour: 'numeric' })
	const { hour12, hourCycle } = format.resolvedOptions()
	const twelveHour = hour12 ?? (hourCycle === 'h11' || hourCycle === 'h12')
	if (!twelveHour) {
		return String(hour)
	}

	const parts = format.formatToParts(new Date(2000, 0, 1, hour))
	const value = parts.find(part => part.type === 'hour')?.value ?? String(hour % 12 || 12)
	const period = parts.find(part => part.type === 'dayPeriod')?.value ?? ''
	return `${value}${period}`
}

/**
 * The time of day at a clock granularity: the locale's hour ("9", "1PM") for an hour, "09:05" for a minute, and
 * "09:05:30" for a second.
 */
export function clockFace(at: Date, granularity: OrbitUnit, locale?: string): string {
	if (granularity === OrbitUnit.Hour) {
		return hourFace(at.getHours(), locale)
	}

	const minute = `${pad2(at.getHours())}:${pad2(at.getMinutes())}`
	return granularity === OrbitUnit.Second ? `${minute}:${pad2(at.getSeconds())}` : minute
}

/** "in 3 weeks", "1 minute ago"; '' beyond `limit` windows either way. */
function countedPhrase(delta: number, unit: string, limit = Number.POSITIVE_INFINITY): string {
	const count = Math.abs(delta)
	if (count > limit) {
		return ''
	}

	const noun = count === 1 ? unit : `${unit}s`
	return delta > 0 ? `in ${count} ${noun}` : `${count} ${noun} ago`
}

/** "This month", "Next week", "Last year" for the window itself and its neighbours; undefined further out. */
function namedPhrase(delta: number, unit: string): string | undefined {
	switch (delta) {
		case 0: return `This ${unit}`
		case 1: return `Next ${unit}`
		case -1: return `Last ${unit}`
		default: return undefined
	}
}

/** "Today", "Tomorrow", "Yesterday", a weekday within the coming week or "Last <weekday>" within the past one, each with `clause`; '' beyond. */
function dayPhrase(at: Date, now: Date, clause: string, locale?: string): string {
	const days = civilDay(at) - civilDay(now)
	switch (days) {
		case 0: return `Today${clause}`
		case 1: return `Tomorrow${clause}`
		case -1: return `Yesterday${clause}`
	}

	const weekday = at.toLocaleDateString(locale, { weekday: 'long' })
	if (days >= 2 && days <= 6) {
		return `${weekday}${clause}`
	}

	return days <= -2 && days >= -6 ? `Last ${weekday}${clause}` : ''
}

/**
 * A relative reading of a moment at a granularity. Inside the moment's own window it reads as that window — "Now",
 * "This hour", "Today", "This week", "This month", "This year" — rather than as past or ahead. Further out it speaks in
 * the granularity's own references: its neighbouring windows by name ("Next hour", "Last week", "Next month"), a
 * count of them within reach ("in 3 weeks", "2 months ago"), and for a clock granularity a day reference with the
 * time at that accuracy ("Tomorrow at 9", "Yesterday at 12:19:05"). Returns '' beyond its reach, where the caller
 * falls back to the absolute reading.
 */
export function relativeGrainedLabel(at: Date, granularity: OrbitUnit, calendar: DeclarativeCalendar, now: Date, locale?: string): string {
	const delta = windowIndex(at, granularity, calendar) - windowIndex(now, granularity, calendar)
	switch (granularity) {
		case OrbitUnit.Year:
			return namedPhrase(delta, 'year') ?? ''
		case OrbitUnit.Month:
			// Within the year on either side: 11 months on the Gregorian calendar, 5 on the six-month Pleiadean one.
			return namedPhrase(delta, 'month') ?? countedPhrase(delta, 'month', calendar === DeclarativeCalendar.Gregorian ? 11 : 5)
		case OrbitUnit.Week:
			return namedPhrase(delta, 'week') ?? countedPhrase(delta, 'week', 4)
		case OrbitUnit.Day:
			return dayPhrase(at, now, '', locale)
	}

	if (delta === 0) {
		return granularity === OrbitUnit.Hour ? 'This hour' : 'Now'
	}

	if (civilDay(at) !== civilDay(now)) {
		return dayPhrase(at, now, ` at ${clockFace(at, granularity, locale)}`, locale)
	}

	if (granularity === OrbitUnit.Hour) {
		return namedPhrase(delta, 'hour') ?? countedPhrase(delta, 'hour')
	}

	// The same day, at a finer granularity: the distance in the coarsest unit that still reads as close, never finer
	// than the granularity itself.
	const seconds = windowIndex(at, OrbitUnit.Second, calendar) - windowIndex(now, OrbitUnit.Second, calendar)
	if (granularity === OrbitUnit.Second && Math.abs(seconds) < 60) {
		return countedPhrase(seconds, 'second')
	}

	const minutes = windowIndex(at, OrbitUnit.Minute, calendar) - windowIndex(now, OrbitUnit.Minute, calendar)
	if (Math.abs(minutes) < 60) {
		return countedPhrase(minutes, 'minute')
	}

	return countedPhrase(windowIndex(at, OrbitUnit.Hour, calendar) - windowIndex(now, OrbitUnit.Hour, calendar), 'hour')
}

/** A civil day spelled in the locale, as a Gregorian date: "Sep 26, 2026". */
function gregorianDay(day: number, locale?: string, style: 'medium' | 'full' = 'medium'): string {
	return civilDayDate(day).toLocaleDateString(locale, { dateStyle: style, timeZone: 'UTC' })
}

/** A window's Gregorian span, "Jul 21, 2026 – Sep 19, 2026", for a tooltip. */
function gregorianSpan(window: CivilWindow, locale?: string): string {
	return `${gregorianDay(window.start, locale)} – ${gregorianDay(window.end - 1, locale)}`
}

/**
 * The compact face of a month or year window on the calendar: "Sol 3" or "Sep 2026" for a month, "Year 3" or "2026"
 * for a year.
 */
export function calendarWindowFace(day: number, granularity: OrbitUnit.Month | OrbitUnit.Year, calendar: DeclarativeCalendar, locale?: string): string {
	if (calendar === DeclarativeCalendar.Gregorian) {
		return civilDayDate(day).toLocaleDateString(locale, granularity === OrbitUnit.Month
			? { month: 'short', year: 'numeric', timeZone: 'UTC' }
			: { year: 'numeric', timeZone: 'UTC' })
	}

	const date = PleiadeanDate.fromDate(civilDayDate(day))
	return granularity === OrbitUnit.Month ? `${date.shortMonthName} ${date.year}` : `Year ${date.year}`
}

/**
 * The spelled-out reading of a week, month or year window, for a tooltip: "Week of Saturday, September 26, 2026",
 * "September 2026", or — for a Pleiadean month or year, whose bounds no Gregorian reader knows — its name with its
 * Gregorian span, "Solaria 3 · Jul 21, 2026 – Sep 19, 2026".
 */
export function calendarWindowLabel(day: number, granularity: OrbitUnit.Week | OrbitUnit.Month | OrbitUnit.Year, calendar: DeclarativeCalendar, locale?: string): string {
	if (granularity === OrbitUnit.Week) {
		return `Week of ${gregorianDay(weekWindow(day, calendar).start, locale, 'full')}`
	}

	if (calendar === DeclarativeCalendar.Gregorian) {
		return civilDayDate(day).toLocaleDateString(locale, granularity === OrbitUnit.Month
			? { month: 'long', year: 'numeric', timeZone: 'UTC' }
			: { year: 'numeric', timeZone: 'UTC' })
	}

	const date = PleiadeanDate.fromDate(civilDayDate(day))
	return granularity === OrbitUnit.Month
		? `${date.monthName} ${date.year} · ${gregorianSpan(monthWindow(day, calendar), locale)}`
		: `Year ${date.year} · ${gregorianSpan(yearWindow(day, calendar), locale)}`
}
