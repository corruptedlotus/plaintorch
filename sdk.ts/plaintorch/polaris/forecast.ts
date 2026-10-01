/*
 * Day arithmetic for planning Polaris cycles day by day — the forecast surface. A day is an ISO date string
 * (`2026-09-30`) of the LOCAL calendar, the same form a cycle's `day` takes, so days compare and sort as plain strings.
 */

/** The ISO date of the local day a moment falls on. */
export function isoDay(date: Date): string {
	const month = String(date.getMonth() + 1).padStart(2, "0")
	const day = String(date.getDate()).padStart(2, "0")
	return `${String(date.getFullYear()).padStart(4, "0")}-${month}-${day}`
}

/** The local midnight an ISO date names. */
export function dateOfIsoDay(day: string): Date {
	const [year, month, date] = day.split("-").map(Number)
	return new Date(year!, month! - 1, date!)
}

/** The ISO date `days` after `day` — before it, when negative. */
export function addIsoDays(day: string, days: number): string {
	const date = dateOfIsoDay(day)
	date.setDate(date.getDate() + days)
	return isoDay(date)
}

/**
 * Whole days from one ISO date to another, negative when `to` comes first. Rounded, so a daylight-saving shift
 * between the two — a 23 or 25 hour day — still counts as one day.
 */
export function isoDaysBetween(from: string, to: string): number {
	return Math.round((dateOfIsoDay(to).getTime() - dateOfIsoDay(from).getTime()) / 86_400_000)
}

/**
 * The last day a forecast surface offers for planning, counting from `today`: the first `minimumDays` days always
 * (today among them), and — whenever a day from today on holds a cycle — up to the day after the last such day, so
 * there is always one more day to plan past the plan. Every day from today to the horizon that has no cycle is
 * offered for planning; later days are not, and earlier ones only show what they hold.
 *
 * @param cycleDays The days that hold a cycle — earlier days among them are ignored.
 */
export function forecastHorizon(today: string, cycleDays: Iterable<string>, minimumDays = 3): string {
	let horizon = addIsoDays(today, Math.max(1, minimumDays) - 1)
	for (const day of cycleDays) {
		if (day >= today) {
			const after = addIsoDays(day, 1)
			if (after > horizon) {
				horizon = after
			}
		}
	}

	return horizon
}
