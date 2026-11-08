import uFuzzy from '@leeoniya/ufuzzy'

/**
 * uFuzzy's universal variant, so a Persian or Cyrillic title splits into words and matches the way a Latin one does.
 * Texts are folded (see {@link foldSearchText}) before they get here, so case and accents are already out of the way.
 */
const universal: uFuzzy.Options = {
	unicode: true,
	interSplit: "[^\\p{L}\\d']+",
	intraSplit: "\\p{Ll}\\p{Lu}",
	intraBound: "\\p{L}\\d|\\d\\p{L}|\\p{Ll}\\p{Lu}",
	intraChars: "[\\p{L}\\d']",
	intraContr: "'\\p{L}{1,2}\\b",
	// Equally good matches keep the order the items came in, rather than falling back to alphabetical.
	compare: () => 0,
}

/**
 * Each typed word forgives one slip after its first letter — a wrong, missing, extra or swapped letter, so "polaros"
 * and "wathcer" still land. Words of four letters or fewer are held stricter, so a short word does not match noise.
 */
const forgiving = new uFuzzy({ ...universal, intraMode: 1 })

/** Each typed word's letters in order inside one word of the text, however far apart — "plr" finds "Polaris". */
const abbreviating = new uFuzzy({ ...universal, intraIns: Infinity })

/** Typed words are tried in every order up to this many, so "quest sky" finds "Sky Quest"; past it they match unranked. */
const anyOrderWords = 5

/**
 * Folds a text for matching: decomposed, stripped of combining marks, and lower-cased, so "Café" reads as "cafe" and
 * "Xuntaš" as "xuntas". The core folds its server-side search the same way.
 */
export function foldSearchText(text: string): string {
	return text.normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase()
}

/**
 * Filters and orders items by how well their text matches a query, best first, ignoring case and accents.
 *
 * Every typed word must turn up in the text, in any order, each forgiving a typo; quoting a phrase demands it
 * verbatim, and a `-word` excludes what holds it. When nothing matches that way, a word may instead be
 * abbreviated — its letters in order within one word of the text. Equally good matches keep the given order,
 * and an empty query keeps every item in it.
 */
export function fuzzyFilter<T>(query: string, items: readonly T[], text: (item: T) => string): T[] {
	const needle = foldSearchText(query.trim())
	if (needle.length === 0) {
		return [...items]
	}

	const haystack = items.map(item => foldSearchText(text(item)))
	const found = rank(forgiving, haystack, needle)
	return (found.length > 0 ? found : rank(abbreviating, haystack, needle)).map(index => items[index]!)
}

/** The haystack indexes a matcher finds for the needle, best first. */
function rank(matcher: uFuzzy, haystack: string[], needle: string): number[] {
	// An infinite ranking threshold: the lists searched here are small enough to always rank in full.
	const [indexes, info, order] = matcher.search(haystack, needle, anyOrderWords, Infinity)
	if (!indexes) {
		return []
	}

	return info && order ? order.map(position => info.idx[position]!) : indexes
}
