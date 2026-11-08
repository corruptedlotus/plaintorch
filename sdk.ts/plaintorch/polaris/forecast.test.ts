import { describe, expect, it } from "vitest"
import { addIsoDays, dateOfIsoDay, forecastHorizon, isoDay, isoDaysBetween } from "./forecast"

describe("forecast day arithmetic", () => {
	it("names the local day a moment falls on", () => {
		expect(isoDay(new Date(2026, 8, 30, 23, 59))).toBe("2026-09-30")
		expect(isoDay(new Date(2026, 0, 5))).toBe("2026-01-05")
	})

	it("reads an ISO date back as that day's local midnight", () => {
		const midnight = dateOfIsoDay("2026-10-03")
		expect([midnight.getFullYear(), midnight.getMonth(), midnight.getDate(), midnight.getHours()]).toEqual([2026, 9, 3, 0])
		expect(isoDay(midnight)).toBe("2026-10-03")
	})

	it("steps across month and year ends, both ways", () => {
		expect(addIsoDays("2026-09-30", 1)).toBe("2026-10-01")
		expect(addIsoDays("2026-12-31", 1)).toBe("2027-01-01")
		expect(addIsoDays("2027-01-01", -1)).toBe("2026-12-31")
		expect(addIsoDays("2028-02-28", 1)).toBe("2028-02-29")
	})

	it("counts whole days between two dates, through daylight-saving shifts", () => {
		expect(isoDaysBetween("2026-09-30", "2026-10-03")).toBe(3)
		expect(isoDaysBetween("2026-10-03", "2026-09-30")).toBe(-3)
		expect(isoDaysBetween("2026-09-30", "2026-09-30")).toBe(0)
		// Spans both of the year's clock changes wherever the test runs.
		expect(isoDaysBetween("2026-03-01", "2026-11-30")).toBe(274)
	})
})

describe("forecastHorizon", () => {
	const today = "2026-09-30"

	it("offers the first three days with nothing planned", () => {
		expect(forecastHorizon(today, [])).toBe("2026-10-02")
	})

	it("stays put while the plan ends inside the first days", () => {
		expect(forecastHorizon(today, [today, "2026-10-01"])).toBe("2026-10-02")
	})

	it("offers one more day once the last offered day is planned", () => {
		expect(forecastHorizon(today, ["2026-10-02"])).toBe("2026-10-03")
		expect(forecastHorizon(today, ["2026-10-02", "2026-10-03"])).toBe("2026-10-04")
	})

	it("reaches past a plan far ahead, offering every day before it", () => {
		expect(forecastHorizon(today, ["2026-10-01", "2026-10-12"])).toBe("2026-10-13")
	})

	it("ignores cycles of days already past", () => {
		expect(forecastHorizon(today, ["2026-09-29", "2026-09-10"])).toBe("2026-10-02")
	})

	it("honours a smaller or larger minimum", () => {
		expect(forecastHorizon(today, [], 1)).toBe(today)
		expect(forecastHorizon(today, [today], 1)).toBe("2026-10-01")
		expect(forecastHorizon(today, [], 7)).toBe("2026-10-06")
		expect(forecastHorizon(today, [], 0)).toBe(today)
	})
})
