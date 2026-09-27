import { DeclarativeCalendar } from "../declaratives/models"
import type { PreferenceView } from "./contracts"

/** Storage keys of the preferences a client reads by name, mirroring the core's `PreferenceKeys` (PEP116). */
export const PreferenceKeys = {
	/** The vault-wide calendar an orbit resolves on when its entity names none; an Enum of `DeclarativeCalendar` names. */
	defaultCalendar: "agenda.default-calendar"
} as const

/**
 * The vault's preferred calendar, read from a preference listing (PEP116): the calendar an orbit resolves on when its
 * entity names none. `undefined` while the listing is absent, or when it carries no readable value.
 */
export function preferredCalendar(preferences: readonly PreferenceView[] | undefined): DeclarativeCalendar | undefined {
	const value = preferences?.find(preference => preference.key === PreferenceKeys.defaultCalendar)?.value
	if (typeof value !== "string") {
		return undefined
	}

	// Enums travel by member name; a numeric enum's reverse mapping would also answer a digit string with a name.
	const calendar = DeclarativeCalendar[value as keyof typeof DeclarativeCalendar]
	return typeof calendar === "number" ? calendar : undefined
}

/**
 * The calendar an entity's orbit resolves on: its own when it names one, else the vault's preferred calendar, else
 * the core's default (Pleiadean) while the preference is unknown. The client mirror of the core's
 * `PlaintorchOrbitService.ResolveCalendar`, so a surface reads an orbit on the calendar the core resolves it on.
 */
export function resolveEntityCalendar(explicit: DeclarativeCalendar | null | undefined, preferred: DeclarativeCalendar | undefined): DeclarativeCalendar {
	return explicit ?? preferred ?? DeclarativeCalendar.Pleiadean
}
