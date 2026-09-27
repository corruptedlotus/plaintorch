// A CalendarSystem that names months the Pleiadean way (six months) and starts the 7-day week on Saturday,
// delegating all date ARITHMETIC to a Gregorian calendar. The humanisers only ask it for names (getUnitName,
// getUnitShortName) and for how far a unit runs (max, when a stepped index is spelled out), so the arithmetic is
// never exercised — this is a NAMING calendar, not a resolution-grade one.
//
// Plugin-authored. humanizeOrbit reads an orbit on it when the orbit's entity resolves on the Pleiadean calendar
// (its own calendar, else the vault's preferred one), so the reading names what the core resolves; the preview
// tool uses it too. A real Pleiadean CalendarSystem (with its own month lengths and epoch) belongs upstream in
// orbit-scheduler.

import type { TimeUnit } from './ast'
import { CalendarSystem, GregorianCalendar } from './calendar'

// Canonical names (from PleiadeanDate / PleiadeanCalendar). The curated short forms are Nil/Sol/Xun/Tar/
// Lua/Tva — NOT plain 3-letter clips (Lunaria clips to "Lun", Tārvan to "Tār") — so they are supplied
// explicitly via getUnitShortName rather than left to the realizer's clip().
const PLEIADEAN_MONTHS = ['Niloumehr', 'Solaria', 'Xuntaš', 'Tarāxriz', 'Lunaria', 'Tārvan']
const PLEIADEAN_MONTH_SHORTS = ['Nil', 'Sol', 'Xun', 'Tar', 'Lua', 'Tva']
// The Pleiadean week starts on Saturday, so day-index 1 is Saturday (a calendar is more than month names).
const WEEKDAYS = ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']

export class PleiadeanNamingCalendar implements CalendarSystem {
	private readonly gregorian = new GregorianCalendar()

	public get(date: Date, unit: TimeUnit, parent?: TimeUnit): number { return this.gregorian.get(date, unit, parent) }
	public set(date: Date, unit: TimeUnit, value: number): Date { return this.gregorian.set(date, unit, value) }
	public add(date: Date, unit: TimeUnit, amount: number): Date { return this.gregorian.add(date, unit, amount) }
	public min(unit: TimeUnit, context: Date): number { return this.gregorian.min(unit, context) }
	// How far a unit runs is a naming-level fact here: a Pleiadean month has up to 61 days, and so up to 10 of its
	// Saturday-first weeks; a year has six months.
	public max(unit: TimeUnit, context: Date): number {
		if (unit === 'M') return PLEIADEAN_MONTHS.length
		if (unit === 'd') return 61
		if (unit === 'w') return 10
		return this.gregorian.max(unit, context)
	}
	public delta(d1: Date, d2: Date, unit: TimeUnit): number { return this.gregorian.delta(d1, d2, unit) }
	public snapToStart(date: Date, unit: TimeUnit): Date { return this.gregorian.snapToStart(date, unit) }

	public getUnitName(unit: TimeUnit, value: number, parent?: TimeUnit): string | null {
		if (unit === 'd' && parent === 'w') return WEEKDAYS[value - 1] ?? null
		if (unit === 'M' && parent === 'y') return PLEIADEAN_MONTHS[value - 1] ?? null
		return null
	}

	// Weekday shorts clip cleanly (Saturday -> "Sat"), so only the months need a curated table.
	public getUnitShortName(unit: TimeUnit, value: number, parent?: TimeUnit): string | null {
		if (unit === 'M' && parent === 'y') return PLEIADEAN_MONTH_SHORTS[value - 1] ?? null
		return null
	}
}
