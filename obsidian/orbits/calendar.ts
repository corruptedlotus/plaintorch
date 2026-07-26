// @ts-nocheck
// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/calendar.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. @ts-nocheck keeps the plugin's strict tsconfig off
// upstream code (mirrors assets/icons/index.ts).

import { TimeUnit } from './ast'

export interface CalendarSystem {
	get(date: Date, unit: TimeUnit, parent?: TimeUnit): number
	set(date: Date, unit: TimeUnit, value: number): Date
	add(date: Date, unit: TimeUnit, amount: number): Date
	min(unit: TimeUnit, context: Date): number
	max(unit: TimeUnit, context: Date): number
	delta(d1: Date, d2: Date, unit: TimeUnit): number;
	snapToStart(date: Date, unit: TimeUnit): Date
	getUnitName(unit: TimeUnit, value: number, parent?: TimeUnit): string | null
}

export class GregorianCalendar implements CalendarSystem {
	private readonly DAY_NAMES = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
	private readonly MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']

	public get(date: Date, unit: TimeUnit, parent?: TimeUnit): number {
		switch (unit) {
			case 'y': return date.getUTCFullYear()
			case 'M': return date.getUTCMonth() + 1
			case 'w': {
				const firstDayOfMonth = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))
				const firstDayOfWeek = firstDayOfMonth.getUTCDay() || 7
				return Math.ceil((date.getUTCDate() + firstDayOfWeek - 1) / 7)
			}
			case 'd':
				if (parent === 'w') {
					return date.getUTCDay() || 7 // 1 = Monday, 7 = Sunday
				}
				return date.getUTCDate()
			case 'h': return date.getUTCHours()
			case 'm': return date.getUTCMinutes()
			case 's': return date.getUTCSeconds()
		}
	}

	public set(date: Date, unit: TimeUnit, value: number): Date {
		const d = new Date(date.getTime())
		switch (unit) {
			case 'y':
				d.setUTCFullYear(value)
				break
			case 'M':
				d.setUTCMonth(value - 1)
				break
			case 'w': {
				const currentWeek = this.get(d, 'w')
				d.setUTCDate(d.getUTCDate() + (value - currentWeek) * 7)
				break
			}
			case 'd':
				d.setUTCDate(value)
				break
			case 'h':
				d.setUTCHours(value)
				break
			case 'm':
				d.setUTCMinutes(value)
				break
			case 's':
				d.setUTCSeconds(value)
				break
		}
		return d
	}

	public add(date: Date, unit: TimeUnit, amount: number): Date {
		const d = new Date(date.getTime())
		switch (unit) {
			case 'y':
				d.setUTCFullYear(d.getUTCFullYear() + amount)
				break
			case 'M':
				d.setUTCMonth(d.getUTCMonth() + amount)
				break
			case 'w':
				d.setUTCDate(d.getUTCDate() + amount * 7)
				break
			case 'd':
				d.setUTCDate(d.getUTCDate() + amount)
				break
			case 'h':
				d.setUTCHours(d.getUTCHours() + amount)
				break
			case 'm':
				d.setUTCMinutes(d.getUTCMinutes() + amount)
				break
			case 's':
				d.setUTCSeconds(d.getUTCSeconds() + amount)
				break
		}
		return d
	}

	public min(unit: TimeUnit, context: Date): number {
		return (unit === 'h' || unit === 'm' || unit === 's') ? 0 : 1
	}

	public max(unit: TimeUnit, context: Date): number {
		switch (unit) {
			case 'y': return 9999
			case 'M': return 12
			case 'w': return 6 // Accounts safely for partial spillover weeks
			case 'd': return new Date(Date.UTC(context.getUTCFullYear(), context.getUTCMonth() + 1, 0)).getUTCDate()
			case 'h': return 23
			case 'm': return 59
			case 's': return 59
		}
	}

	public snapToStart(date: Date, unit: TimeUnit): Date {
		const d = new Date(date.getTime())
		if (unit === 'y') {
			d.setUTCMonth(0, 1)
			d.setUTCHours(0, 0, 0, 0)
		} else if (unit === 'M') {
			d.setUTCDate(1)
			d.setUTCHours(0, 0, 0, 0)
		} else if (unit === 'w') {
			const currentDayOfWeek = this.get(d, 'd', 'w')
			d.setUTCDate(d.getUTCDate() - (currentDayOfWeek - 1))
			d.setUTCHours(0, 0, 0, 0)
		} else if (unit === 'd') {
			d.setUTCHours(0, 0, 0, 0)
		} else if (unit === 'h') {
			d.setUTCMinutes(0, 0, 0)
		} else if (unit === 'm') {
			d.setUTCSeconds(0, 0)
		} else if (unit === 's') {
			d.setUTCMilliseconds(0)
		}
		return d
	}

	public getUnitName(unit: TimeUnit, value: number, parent?: TimeUnit): string | null {
		if (unit === 'd' && parent === 'w') return this.DAY_NAMES[value - 1] || null
		if (unit === 'M' && parent === 'y') return this.MONTH_NAMES[value - 1] || null
		return null
	}

	public delta(d1: Date, d2: Date, unit: TimeUnit): number {
		const sign = d1.getTime() >= d2.getTime() ? 1 : -1;
		const start = d1.getTime() >= d2.getTime() ? d2 : d1;
		const end = d1.getTime() >= d2.getTime() ? d1 : d2;

		switch (unit) {
			case 'y':
				return sign * (end.getUTCFullYear() - start.getUTCFullYear());
			case 'M':
				return sign * ((end.getUTCFullYear() - start.getUTCFullYear()) * 12 + (end.getUTCMonth() - start.getUTCMonth()));
			case 'w': {
				const w1 = this.snapToStart(start, 'w').getTime();
				const w2 = this.snapToStart(end, 'w').getTime();
				return sign * Math.round((w2 - w1) / 604800000); // 7 days in ms
			}
			case 'd': {
				const d1Ms = this.snapToStart(start, 'd').getTime();
				const d2Ms = this.snapToStart(end, 'd').getTime();
				return sign * Math.round((d2Ms - d1Ms) / 86400000); // 1 day in ms
			}
			case 'h': {
				const h1 = this.snapToStart(start, 'h').getTime();
				const h2 = this.snapToStart(end, 'h').getTime();
				return sign * Math.round((h2 - h1) / 3600000);
			}
			case 'm': {
				const m1 = this.snapToStart(start, 'm').getTime();
				const m2 = this.snapToStart(end, 'm').getTime();
				return sign * Math.round((m2 - m1) / 60000);
			}
			case 's': {
				const s1 = this.snapToStart(start, 's').getTime();
				const s2 = this.snapToStart(end, 's').getTime();
				return sign * Math.round((s2 - s1) / 1000);
			}
		}
	}
}
