import { epochOnLocalClock, type Epoch } from '@pleiades/sdk'
import { zoneCity } from '../editing/TimeZoneSelect'

/** Where an occurrence is anchored when that is another clock than this device's. */
export interface OccurrenceAnchor {
	/** The zone's city, as a tag names it ("Tehran"). */
	readonly city: string
	/** The occurrence's own wall time there ("18:30 in Tehran"), for a tooltip. */
	readonly label: string
	/** The zone itself ("Asia/Tehran"). */
	readonly zone: string
}

/** An occurrence's time as this device reads it, with the anchoring zone when that is another one. */
export interface OccurrenceTime {
	readonly date: string
	readonly time: string | undefined
	readonly anchor: OccurrenceAnchor | undefined
}

/** The zone this device keeps its clock in. */
const deviceZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone

/**
 * Where an occurrence falls on this device's clock — a zoned timed one at the instant it names, read here, so a
 * Tehran 18:30 reads 16:30 in Berlin — with the anchoring zone and its own wall time when that zone is not this
 * device's. A floating or all-day occurrence reads as written and names no anchor.
 */
export function occurrenceTime(epoch: Epoch): OccurrenceTime {
	const local = epochOnLocalClock(epoch)
	const zone = epoch.instant ? epoch.timeZone ?? undefined : undefined
	const anchor = zone && zone !== deviceZone() && epoch.timeOfDay
		? { zone, city: zoneCity(zone), label: `${epoch.timeOfDay.slice(0, 5)} in ${zoneCity(zone)}` }
		: undefined
	return { date: local.date, time: local.timeOfDay, anchor }
}
