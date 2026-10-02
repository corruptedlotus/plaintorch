import type { EntityIdentifier } from "./identity"

const maximumDepth = 8

/**
 * Determines whether an existing value is indistinguishable from an incoming one.
 *
 * This is what keeps a response that changed nothing from re-rendering the surfaces showing it, and what
 * lets unchanged arrays and objects keep their references across a refetch.
 *
 * Entities short-circuit on identity rather than being compared field by field. That is both correct —
 * the store has already reconciled them, and they notify their own observers — and what keeps this
 * terminating, since every cycle in the canonical graph runs through an entity reference. Which values are
 * entities is the store's policy, so the caller supplies it; {@link EntityStore.isEquivalent} is this with
 * the store's own.
 */
export function isEquivalent(a: unknown, b: unknown, identify: EntityIdentifier, depth = 0): boolean {
	if (a === b) {
		return true
	}

	if (depth > maximumDepth || !a || !b || typeof a !== "object" || typeof b !== "object") {
		return false
	}

	const keyA = identify(a)
	if (keyA !== undefined || identify(b) !== undefined) {
		return keyA === identify(b)
	}

	if (Array.isArray(a) || Array.isArray(b)) {
		return Array.isArray(a)
			&& Array.isArray(b)
			&& a.length === b.length
			&& a.every((item, index) => isEquivalent(item, b[index], identify, depth + 1))
	}

	const entriesA = Object.entries(a)
	const entriesB = Object.entries(b)
	return entriesA.length === entriesB.length
		&& entriesA.every(([key, value]) => key in b && isEquivalent(value, (b as Record<string, unknown>)[key], identify, depth + 1))
}
