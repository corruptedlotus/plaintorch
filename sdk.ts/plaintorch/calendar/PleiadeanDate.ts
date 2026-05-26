const monthNames = ["Niloumehr", "Solaria", "Xuntaš", "Tarāxriz", "Lunaria", "Tārvan"] as const
const dayMilliseconds = 24 * 60 * 60 * 1000
const epochUtcMs = Date.UTC(2023, 2, 21)

const persianFormatter = new Intl.DateTimeFormat("en-u-ca-persian-nu-latn", {
	timeZone: "UTC",
	year: "numeric",
	month: "numeric",
	day: "numeric"
})

const persianYearStartCache = new Map<number, Date>()

type PersianParts = {
	year: number
	month: number
	day: number
}

export type PleiadeanYearType = "blood" | "sweat" | "tears"

export class PleiadeanDate {
	public readonly year: number
	public readonly month: number
	public readonly day: number

	public constructor(year: number, month: number, day: number) {
		if (!Number.isInteger(year)) {
			throw new RangeError("Year must be an integer")
		}

		if (!Number.isInteger(month) || month < 1 || month > 6) {
			throw new RangeError("Month must be between 1 and 6")
		}

		const monthDays = PleiadeanDate.getDaysInMonth(year, month)
		if (!Number.isInteger(day) || day < 1 || day > monthDays) {
			throw new RangeError(`Day must be between 1 and ${monthDays}`)
		}

		this.year = year
		this.month = month
		this.day = day
	}

	public get monthName(): string {
		return monthNames[this.month - 1]!
	}

	public get yearType(): PleiadeanYearType {
		const mark = this.year % 3
		switch (mark) {
			case 2:
				return "blood"
			case 1:
				return "sweat"
			default:
				return "tears"
		}
	}

	public toDate(): Date {
		let daysOffset = 0
		if (this.year >= 0) {
			for (let y = 0; y < this.year; y++) {
				daysOffset += PleiadeanDate.getDaysInYear(y)
			}
		}
		else {
			for (let y = -1; y >= this.year; y--) {
				daysOffset -= PleiadeanDate.getDaysInYear(y)
			}
		}

		for (let m = 1; m < this.month; m++) {
			daysOffset += PleiadeanDate.getDaysInMonth(this.year, m)
		}

		daysOffset += this.day - 1
		return new Date(epochUtcMs + daysOffset * dayMilliseconds)
	}

	public static fromDate(date: Date): PleiadeanDate {
		if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
			throw new TypeError("A valid Date is required")
		}

		const utcDayMs = Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate())
		let daysSinceEpoch = Math.floor((utcDayMs - epochUtcMs) / dayMilliseconds)
		let year = 0

		if (daysSinceEpoch >= 0) {
			while (true) {
				const yearDays = PleiadeanDate.getDaysInYear(year)
				if (daysSinceEpoch < yearDays) {
					break
				}

				daysSinceEpoch -= yearDays
				year++
			}
		}
		else {
			while (daysSinceEpoch < 0) {
				year--
				daysSinceEpoch += PleiadeanDate.getDaysInYear(year)
			}
		}

		let month = 1
		while (month <= 6) {
			const monthDays = PleiadeanDate.getDaysInMonth(year, month)
			if (daysSinceEpoch < monthDays) {
				break
			}

			daysSinceEpoch -= monthDays
			month++
		}

		const day = daysSinceEpoch + 1
		return new PleiadeanDate(year, month, day)
	}

	public static getDaysInYear(year: number): number {
		return 5 * 61 + (PleiadeanDate.isLeapYear(year) ? 61 : 60)
	}

	public static getDaysInMonth(year: number, month: number): number {
		if (!Number.isInteger(month) || month < 1 || month > 6) {
			throw new RangeError("Month must be between 1 and 6")
		}

		if (month <= 5) {
			return 61
		}

		return PleiadeanDate.isLeapYear(year) ? 61 : 60
	}

	public static isLeapYear(year: number): boolean {
		const persianYear = 1402 + year
		const thisStart = resolvePersianYearStartUtc(persianYear)
		const nextStart = resolvePersianYearStartUtc(persianYear + 1)
		const daySpan = Math.floor((nextStart.getTime() - thisStart.getTime()) / dayMilliseconds)
		return daySpan === 366
	}
}

function resolvePersianYearStartUtc(persianYear: number): Date {
	const cached = persianYearStartCache.get(persianYear)
	if (cached) {
		return cached
	}

	const gregorianYear = persianYear + 621
	for (let day = 15; day <= 25; day++) {
		const candidate = new Date(Date.UTC(gregorianYear, 2, day))
		const parts = readPersianParts(candidate)
		if (parts.year === persianYear && parts.month === 1 && parts.day === 1) {
			persianYearStartCache.set(persianYear, candidate)
			return candidate
		}
	}

	throw new Error(`Failed to resolve Persian year start for ${persianYear}`)
}

function readPersianParts(date: Date): PersianParts {
	const parts = persianFormatter.formatToParts(date)
	const yearText = parts.find((part) => part.type === "year")?.value
	const monthText = parts.find((part) => part.type === "month")?.value
	const dayText = parts.find((part) => part.type === "day")?.value

	if (!yearText || !monthText || !dayText) {
		throw new Error("Unable to parse Persian calendar date parts")
	}

	return {
		year: Number.parseInt(yearText, 10),
		month: Number.parseInt(monthText, 10),
		day: Number.parseInt(dayText, 10)
	}
}
