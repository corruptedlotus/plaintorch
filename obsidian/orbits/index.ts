// Plugin-facing surface over the vendored @pleiades/orbits stack. The parser,
// humanizer and calendar are kept verbatim from orbit-scheduler; only the helpers
// below are plugin-specific.

export * from './ast'
export * from './calendar'
export * from './parser'
export * from './humanizer'
export * from './shortHumanizer'

import { GregorianCalendar } from './calendar'
import { OrbitHumanizer } from './humanizer'
import { OrbitParser } from './parser'
import { OrbitShortHumanizer } from './shortHumanizer'

// The humanizers and calendar are stateless, so a single shared instance of each is enough.
const sharedHumanizer = new OrbitHumanizer(new GregorianCalendar())
const sharedShortHumanizer = new OrbitShortHumanizer(new GregorianCalendar())

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
 * Pass `short` for the terse, space-friendly reading ("Every 3 Wed @12:00") from the plugin's
 * {@link OrbitShortHumanizer}; omit it for the full one ("Wednesdays of every 3 weeks at 12:00")
 * from the vendored humanizer. Should the short serializer trip on some exotic notation it does
 * not shorten, it falls back to the full phrase rather than the raw string.
 *
 * Returns the raw notation (flagged invalid) when the notation cannot be parsed at all, so
 * callers can still show something meaningful while a user is mid-edit.
 */
export function humanizeOrbit(orbit: string | undefined | null, short = false): HumanizedOrbit {
	const raw = orbit?.trim() ?? ''
	if (!raw) {
		return { text: '', invalid: false }
	}

	try {
		const ast = new OrbitParser(raw).parse()
		const phrase = short ? shortPhrase(ast) : sharedHumanizer.serialize(ast)
		return { text: capitalizeFirst(phrase), invalid: false }
	} catch {
		return { text: raw, invalid: true }
	}
}

/** The terse reading, falling back to the full one if the compact serializer cannot shorten the tree. */
function shortPhrase(ast: ReturnType<OrbitParser['parse']>): string {
	try {
		return sharedShortHumanizer.serialize(ast)
	} catch {
		return sharedHumanizer.serialize(ast)
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
