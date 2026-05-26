export function getOrdinalSuffix(value: number): string {
	if (!Number.isFinite(value)) {
		throw new TypeError("Ordinal suffix requires a finite number")
	}

	const absoluteInteger = Math.trunc(Math.abs(value))
	const lastTwoDigits = absoluteInteger % 100

	if (lastTwoDigits >= 11 && lastTwoDigits <= 13) {
		return "th"
	}

	switch (absoluteInteger % 10) {
		case 1:
			return "st"
		case 2:
			return "nd"
		case 3:
			return "rd"
		default:
			return "th"
	}
}
