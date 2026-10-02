import { DeclarativeCalendar, OrbitUnit, PleiadeanDate } from "@pleiades/sdk"
import { OrbitParser, type DurationPart } from "../../orbits"

/**
 * The scopes a one-shot schedule can take: those a `Z{y/M/d[Th:m[:s]]}` literal writes. A date alone is a whole day;
 * a time makes it a moment to the minute, or to the second when it carries seconds.
 */
export const oneShotGranularities: readonly OrbitUnit[] = [OrbitUnit.Day, OrbitUnit.Minute, OrbitUnit.Second]

/**
 * A schedule of one single occurrence: what a lone `Z{…}` literal says, in the shape the epoch editors
 * ({@link https EditableDateTime}, {@link https EditableDuration}) edit.
 */
export interface OneShot {
	/** The civil moment, `YYYY-MM-DDTHH:mm:ss` on the Gregorian clock the editors keep; midnight for a whole day. */
	readonly moment: string
	/** How much of the moment is real: one of {@link oneShotGranularities}. */
	readonly granularity: OrbitUnit
	/** The block's length in Orbit duration notation (`1h30m`), or none to fill one whole unit. */
	readonly duration?: string
}

/**
 * Reads an orbit that is nothing but a `Z{…}` literal, at most with a `=<dur>` span, as a one-shot — its year, month and
 * day read on `calendar`, the calendar the core reads the literal on. Anything else is not a one-shot and reads as
 * undefined: a recurrence, a literal under a bound, a limit or an interval, a time-of-day `z{…}`, a date the calendar
 * does not have, or notation that does not parse.
 */
export function readOneShot(orbit: string | undefined, calendar: DeclarativeCalendar): OneShot | undefined {
	const notation = orbit?.trim()
	if (!notation) {
		return undefined
	}

	let node
	try {
		node = new OrbitParser(notation).parse()
	}
	catch {
		return undefined
	}

	if (node.kind !== 'DateTimeLiteralNode' || node.interval !== undefined || node.limits.length > 0
		|| node.year === undefined || node.month === undefined || node.day === undefined) {
		return undefined
	}

	const date = isoDateOn(calendar, node.year, node.month, node.day)
	const time = node.hour === undefined ? [0, 0, 0] : [node.hour, node.minute ?? 0, node.second ?? 0]
	if (!date || time[0]! > 23 || time[1]! > 59 || time[2]! > 59) {
		return undefined
	}

	return {
		moment: `${date}T${time.map(pad).join(':')}`,
		granularity: node.hour === undefined ? OrbitUnit.Day : node.second === undefined ? OrbitUnit.Minute : OrbitUnit.Second,
		duration: node.duration ? durationNotation(node.duration) : undefined,
	}
}

/**
 * Writes a one-shot as its `Z{…}` literal, its year, month and day on `calendar`: the time to the minute or the second
 * as its scope makes real (none for a whole day), and its span as `=<dur>`. Undefined when the calendar cannot write the
 * day — a Pleiadean date before year 0 began (21 March 2023).
 */
export function writeOneShot(oneShot: OneShot, calendar: DeclarativeCalendar): string | undefined {
	const isoDate = oneShot.moment.slice(0, 10)
	let ymd: readonly number[]
	if (calendar === DeclarativeCalendar.Pleiadean) {
		const pleiadean = PleiadeanDate.tryFromISO(isoDate)
		if (!pleiadean || pleiadean.year < 0) {
			return undefined
		}

		ymd = [pleiadean.year, pleiadean.month, pleiadean.day]
	}
	else {
		ymd = isoDate.split('-').map(Number)
	}

	const [hour = '0', minute = '00', second = '00'] = oneShot.moment.slice(11, 19).split(':')
	const time = oneShot.granularity === OrbitUnit.Day ? ''
		: oneShot.granularity === OrbitUnit.Second ? `T${Number(hour)}:${minute}:${second}`
			: `T${Number(hour)}:${minute}`
	const literal = `Z{${ymd.join('/')}${time}}`
	return oneShot.duration ? `${literal}=${oneShot.duration}` : literal
}

/** The Gregorian `YYYY-MM-DD` of a year, month and day on a calendar; undefined when the calendar has no such day. */
function isoDateOn(calendar: DeclarativeCalendar, year: number, month: number, day: number): string | undefined {
	if (calendar === DeclarativeCalendar.Pleiadean) {
		try {
			return new PleiadeanDate(year, month, day).toISODate()
		}
		catch {
			return undefined
		}
	}

	const date = new Date(Date.UTC(year, month - 1, day))
	date.setUTCFullYear(year)
	return date.getUTCMonth() === month - 1 && date.getUTCDate() === day
		? `${String(year).padStart(4, '0')}-${pad(month)}-${pad(day)}`
		: undefined
}

function durationNotation(parts: readonly DurationPart[]): string {
	return parts.map(part => `${part.count}${part.unit}`).join('')
}

function pad(value: number): string {
	return value.toString().padStart(2, '0')
}
