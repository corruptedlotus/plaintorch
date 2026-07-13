/**
 * Working time units are the whole-minute stamps used by executive time allocations.
 * These helpers translate that minute-based storage form into the shapes consumers need:
 * a clock/timespan string for application surfaces and an hours-and-fraction number for UI.
 */

function assertWorkingTimeUnit(minutes: number): void {
	if (!Number.isFinite(minutes)) {
		throw new TypeError("Working time unit requires a finite number of minutes")
	}

	if (minutes < 0) {
		throw new RangeError("Working time unit cannot be negative")
	}
}

/**
 * Converts a minute-based working time unit into hours as a fraction (90 becomes 1.5).
 */
export function workingTimeUnitToHours(minutes: number): number {
	assertWorkingTimeUnit(minutes)
	return minutes / 60
}

/**
 * Converts a fractional hours value back into a whole-minute working time unit (1.5 becomes 90).
 */
export function hoursToWorkingTimeUnit(hours: number): number {
	if (!Number.isFinite(hours)) {
		throw new TypeError("Working time unit conversion requires a finite number of hours")
	}

	if (hours < 0) {
		throw new RangeError("Working time unit cannot be negative")
	}

	return Math.round(hours * 60)
}

/**
 * Formats a minute-based working time unit as an `H:MM` clock/timespan string (90 becomes "1:30").
 */
export function formatWorkingTimeUnit(minutes: number): string {
	assertWorkingTimeUnit(minutes)
	const whole = Math.trunc(minutes)
	const hours = Math.trunc(whole / 60)
	const remainder = whole % 60
	return `${hours}:${remainder.toString().padStart(2, "0")}`
}

/**
 * Sums a series of optional working time units, skipping unset (null/undefined) entries.
 * Useful for deriving a Polaris cycle's total workload from its executive estimations.
 */
export function sumWorkingTimeUnits(values: Iterable<number | null | undefined>): number {
	let total = 0
	for (const value of values) {
		if (value === null || value === undefined) {
			continue
		}

		assertWorkingTimeUnit(value)
		total += value
	}

	return total
}
