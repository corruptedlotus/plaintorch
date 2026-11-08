import { describe, expect, it } from "vitest"
import { epochOnLocalClock, OrbitUnit, type Epoch } from "./models"

const pad = (value: number) => String(value).padStart(2, "0")

function epoch(overrides: Partial<Epoch>): Epoch {
	return {
		moment: "2026-10-02T18:30:00",
		granularity: OrbitUnit.Minute,
		duration: undefined,
		timeZone: undefined,
		date: "2026-10-02",
		isAllDay: false,
		timeOfDay: "18:30:00",
		endMoment: "2026-10-02T18:31:00",
		...overrides
	}
}

describe("epochOnLocalClock", () => {
	it("reads a zoned timed epoch at its instant, on this device's clock", () => {
		// 18:30 in Tehran (+03:30) is 15:00 UTC.
		const reading = epochOnLocalClock(epoch({ timeZone: "Asia/Tehran", instant: "2026-10-02T15:00:00+00:00" }))

		const local = new Date(Date.UTC(2026, 9, 2, 15, 0, 0))
		expect(reading.date).toBe(`${local.getFullYear()}-${pad(local.getMonth() + 1)}-${pad(local.getDate())}`)
		expect(reading.timeOfDay).toBe(`${pad(local.getHours())}:${pad(local.getMinutes())}:00`)
	})

	it("reads a floating or all-day epoch as written", () => {
		expect(epochOnLocalClock(epoch({}))).toEqual({ date: "2026-10-02", timeOfDay: "18:30:00" })
		expect(epochOnLocalClock(epoch({ granularity: OrbitUnit.Day, isAllDay: true, timeOfDay: undefined, timeZone: "Asia/Tehran" })))
			.toEqual({ date: "2026-10-02", timeOfDay: undefined })
	})
})
