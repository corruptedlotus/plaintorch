/**
 * Scores how well a query matches a text, case-insensitively, as a subsequence: every query character must
 * appear in the text in order, but need not be adjacent — "plr" finds "Polaris", "sky q" finds "Sky Quest".
 *
 * Higher is better; `undefined` is no match. A contiguous run, a hit at the start of a word, and a hit near the
 * start of the text all score up, so "pol" ranks "Polaris" above "Interpolation" and an exact substring above a
 * scattered one. An empty query matches everything at zero.
 */
export function fuzzyScore(query: string, text: string): number | undefined {
	const needle = query.trim().toLowerCase()
	if (needle.length === 0) {
		return 0
	}

	const haystack = text.toLowerCase()
	let score = 0
	let position = 0
	let previous = -2
	for (const character of needle) {
		const found = haystack.indexOf(character, position)
		if (found < 0) {
			return undefined
		}

		score += 1
		if (found === previous + 1) {
			score += 2
		}

		if (found === 0 || /[\s\-_./:]/.test(haystack[found - 1] ?? '')) {
			score += 1.5
		}

		score -= found * 0.01
		previous = found
		position = found + 1
	}

	// A near-exact substring of the text is the strongest signal of all.
	if (haystack.includes(needle)) {
		score += needle.length
	}

	return score
}

/**
 * Filters and orders items by their fuzzy match against a query, best first, keeping the given order for ties.
 * With an empty query every item is kept in its given order.
 */
export function fuzzyFilter<T>(query: string, items: readonly T[], text: (item: T) => string): T[] {
	return items
		.map((item, index) => ({ item, index, score: fuzzyScore(query, text(item)) }))
		.filter((entry): entry is { item: T, index: number, score: number } => entry.score !== undefined)
		.sort((a, b) => b.score - a.score || a.index - b.index)
		.map(entry => entry.item)
}
