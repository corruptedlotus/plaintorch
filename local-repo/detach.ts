import type { EntityIdentifier } from "./identity.js"

/**
 * Takes an instance out of every collection reachable from a value, and reports whether anything was changed.
 *
 * The walk goes down through arrays and plain objects, and stops at any *other* entity it meets: that entity
 * is a root of its own, and whoever walks the store visits it in its own right. So handing this an entity
 * detaches from that entity's own collections, including the ones inside its value objects and child records,
 * and no further.
 *
 * A collection that held the instance is **replaced** on its owner by a new array without it, rather than
 * edited in place — the same way a merge replaces a collection that changed. A surface that passes the
 * collection down as a property then sees a different reference and re-renders, where an array spliced in
 * place would look unchanged to it.
 *
 * Only collections are touched. A single reference that points at the instance is left alone: whether an owner
 * can do without it is the owner's business, and not every shape allows the field to be empty.
 *
 * @returns The value to keep in place of the one handed in — a new array when the value was itself a
 * collection holding the instance, otherwise the same value — and whether anything, at any depth, changed.
 */
export function detachFrom<T>(value: T, target: object, identify: EntityIdentifier): { value: T, changed: boolean } {
	const state = { changed: false }
	const replaced = detach(value, target, identify, new WeakSet(), state, true)
	return { value: replaced as T, changed: state.changed }
}

function detach(
	value: unknown,
	target: object,
	identify: EntityIdentifier,
	visited: WeakSet<object>,
	state: { changed: boolean },
	isRoot: boolean
): unknown {
	if (value === null || typeof value !== "object" || visited.has(value)) {
		return value
	}
	visited.add(value)

	if (Array.isArray(value)) {
		let replaced: unknown[] | undefined
		for (let index = 0; index < value.length; index++) {
			const item = value[index] as unknown
			const kept = item === target ? undefined : detach(item, target, identify, visited, state, false)
			if (item === target || kept !== item) {
				// The first difference: start the replacement from everything kept so far.
				replaced ??= value.slice(0, index)
				state.changed = true
			}

			if (item !== target) {
				replaced?.push(kept)
			}
		}

		return replaced ?? value
	}

	// Another entity is its own root. The value handed in is walked whatever it is.
	if (!isRoot && identify(value) !== undefined) {
		return value
	}

	const holder = value as Record<string, unknown>
	for (const field of Object.keys(holder)) {
		const current = holder[field]
		if (current === null || typeof current !== "object" || current === target) {
			continue
		}

		const kept = detach(current, target, identify, visited, state, false)
		if (kept !== current && isWritable(holder, field)) {
			holder[field] = kept
		}
	}

	return value
}

function isWritable(holder: object, field: string): boolean {
	if (Object.isFrozen(holder)) {
		return false
	}

	const descriptor = Object.getOwnPropertyDescriptor(holder, field)
	return !descriptor || descriptor.writable === true || typeof descriptor.set === "function"
}
