// The pipeline entry point for the model-based humaniser: notation -> AST -> ScheduleModel -> phrase.
// Kept separate from index.ts's legacy humanizeOrbit while we build it out; it will fold in once it covers
// enough shapes. Returns the model too, so callers (and the preview tool) can inspect the extracted meaning.

import { CalendarSystem, GregorianCalendar } from './calendar'
import { OrbitParser } from './parser'
import { PleiadeanNamingCalendar } from './pleiadeanNaming'
import type { ScheduleModel } from './scheduleModel'
import { normalizeSchedule } from './scheduleNormalizer'
import { realizeLong, realizeShort } from './scheduleRealizer'

const defaultCalendar = new GregorianCalendar()

/** Maps a calendar name (the preview tool's --calendar flag) to a CalendarSystem used for naming. */
export function resolveCalendar(name: string | undefined): CalendarSystem {
	switch ((name ?? '').trim().toLowerCase()) {
		case 'pleiadean':
		case 'p':
			return new PleiadeanNamingCalendar()
		default:
			return new GregorianCalendar()
	}
}

export interface OrbitDescription {
	/** The extracted meaning, when the notation parsed and normalized. */
	model?: ScheduleModel
	/** The long-register phrase. */
	long?: string
	/** The short-register phrase. */
	short?: string
	/** Why it could not be described (parse failure, or a shape not yet modelled). */
	error?: string
}

/** Parses, normalizes, and realizes an Orbit notation. Never throws. */
export function describeOrbit(notation: string, calendar: CalendarSystem = defaultCalendar): OrbitDescription {
	try {
		const ast = new OrbitParser(notation).parse()
		const model = normalizeSchedule(ast, calendar)
		return { model, long: realizeLong(model), short: realizeShort(model) }
	} catch (error) {
		return { error: error instanceof Error ? error.message : String(error) }
	}
}
