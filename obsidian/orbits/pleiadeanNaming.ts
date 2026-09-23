// A CalendarSystem for humaniser PREVIEW that names months the Pleiadean way (six months) and keeps the
// universal 7-day week, delegating all date ARITHMETIC to a Gregorian calendar. The humaniser only ever calls
// getUnitName, so the arithmetic is never exercised — this is a NAMING calendar, not a resolution-grade one.
//
// Plugin-authored. It exists so the preview tool can show how an orbit reads under a non-Gregorian calendar;
// a real Pleiadean CalendarSystem (with its own month lengths and epoch) belongs upstream in orbit-scheduler.

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
	public max(unit: TimeUnit, context: Date): number { return this.gregorian.max(unit, context) }
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
