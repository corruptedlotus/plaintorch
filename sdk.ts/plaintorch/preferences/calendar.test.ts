import { describe, expect, it } from "vitest"
import { PlaintorchCoreClient } from "../coreClient"
import { DeclarativeCalendar } from "../declaratives/models"
import type { PlaintorchCoreRequest, PlaintorchCoreResponse, PlaintorchCoreTransport } from "../internal/transport"
import type { PreferenceView } from "./contracts"
import { PreferenceKeys, preferredCalendar, resolveEntityCalendar } from "./calendar"

function calendarPreference(value: string): PreferenceView {
	return {
		key: PreferenceKeys.defaultCalendar,
		label: "Default resolution calendar",
		group: "Agenda",
		kind: "Enum",
		value,
		default: "Pleiadean",
		options: ["Gregorian", "Pleiadean"]
	}
}

/** A client whose transport answers the preference listing with whatever `listing` holds, counting the reads. */
function preferenceClient(listing: () => PreferenceView[]) {
	const reads: string[] = []
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse> {
			reads.push(request.path)
			const body = request.path === "/api/preferences" ? JSON.stringify(listing()) : "null"
			return { ok: true, status: 200, async text() { return body }, header() { return undefined } }
		}
	}

	return { client: new PlaintorchCoreClient({ transports: [transport] }), reads }
}

describe("preferred calendar (PEP116)", () => {
	it("reads the calendar preference by member name", () => {
		expect(preferredCalendar([calendarPreference("Gregorian")])).toBe(DeclarativeCalendar.Gregorian)
		expect(preferredCalendar([calendarPreference("Pleiadean")])).toBe(DeclarativeCalendar.Pleiadean)
	})

	it("is unknown without a listing, or without a readable value", () => {
		expect(preferredCalendar(undefined)).toBeUndefined()
		expect(preferredCalendar([])).toBeUndefined()
		expect(preferredCalendar([calendarPreference("Julian")])).toBeUndefined()
		// A digit string would reverse-map to a member name on a numeric enum; it is not a calendar.
		expect(preferredCalendar([calendarPreference("0")])).toBeUndefined()
	})

	it("lets an entity's own calendar win, then the preference, then the core's Pleiadean default", () => {
		expect(resolveEntityCalendar(DeclarativeCalendar.Gregorian, DeclarativeCalendar.Pleiadean)).toBe(DeclarativeCalendar.Gregorian)
		expect(resolveEntityCalendar(undefined, DeclarativeCalendar.Gregorian)).toBe(DeclarativeCalendar.Gregorian)
		expect(resolveEntityCalendar(null, undefined)).toBe(DeclarativeCalendar.Pleiadean)
	})
})

describe("preferences record (PEP116)", () => {
	it("resolves the core's preference listing", async () => {
		const { client } = preferenceClient(() => [calendarPreference("Gregorian")])

		expect(preferredCalendar(await client.repos.preferences.get())).toBe(DeclarativeCalendar.Gregorian)
	})

	it("follows a preference write the change feed announces, while observed", async () => {
		let value = "Pleiadean"
		const { client, reads } = preferenceClient(() => [calendarPreference(value)])
		let notified = 0
		const unsubscribe = client.repos.preferences.subscribe("", () => notified++)
		await client.repos.preferences.get()

		// The core announces a preference write as a "UserPreference" change keyed by the preference key; no entity
		// repository serves that type, so the announcement only revalidates the observed records.
		value = "Gregorian"
		client.repos.invalidation.invalidate("UserPreference", PreferenceKeys.defaultCalendar)
		await client.repos.invalidation.settled()
		unsubscribe()

		expect(preferredCalendar(client.repos.preferences.peek())).toBe(DeclarativeCalendar.Gregorian)
		expect(reads.filter(path => path === "/api/preferences")).toHaveLength(2)
		expect(notified).toBeGreaterThan(0)
	})
})
