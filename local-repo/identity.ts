/**
 * Runtime type name of a tracked entity, as the backend stamps it on the wire.
 *
 * Deliberately not a closed union. It was one, listing the type names by hand, and the list drifted: the
 * backend's concrete stellar directive is `StellarDirective`, the list said `Directive`, and every directive
 * silently stopped being tracked — an entity that is never absorbed has no canonical instance, so nothing
 * observing it ever hears anything. Which names exist belongs to the backend, so recognizing an entity is
 * structural (see {@link EntityIdentifier}) and only *routing* a name to a repository is declared.
 */
export type EntityTypeName = string

/** Identity of a tracked entity within the store, in the form `{typeName}:{id}`. */
export type EntityKey = string

/**
 * Resolves the store identity of a value, or `undefined` when it is not a tracked entity.
 *
 * What counts as an entity is the consumer's to say, so this is the first thing a store is given. It is asked
 * about every node of every response, raw payloads and constructed model instances alike, so it must be cheap,
 * must accept anything, and must answer the same for a payload and for the instance constructed from it.
 */
export type EntityIdentifier = (value: unknown) => EntityKey | undefined

/**
 * Builds the store identity for a type name and identifier.
 */
export function entityKey(typeName: string, id: string): EntityKey {
	return `${typeName}:${id}`
}

/** The type-name half of a store key. */
export function typeNameFromKey(key: EntityKey): EntityTypeName {
	const separator = key.indexOf(":")
	return separator < 0 ? key : key.slice(0, separator)
}

/** The shape {@link identifyByShape} recognizes an entity by. */
export interface EntityShape {
	/** The field carrying the runtime type name. */
	readonly typeKey: string
	/** The field carrying the identifier. Defaults to `id`. */
	readonly idKey?: string
	/**
	 * A further test a value must pass to be tracked, for a domain where a type name and an identifier are
	 * not enough — one whose value objects carry another entity's identifier as a reference, say.
	 */
	readonly requires?: (value: Record<string, unknown>) => boolean
}

/**
 * Builds an {@link EntityIdentifier} for the common shape: a non-empty string type name under one field and a
 * non-empty string identifier under another.
 *
 * The identifier must be a string. A record keyed on a number is a child reached through its owner rather
 * than something addressed on its own, and is absorbed as part of that owner.
 */
export function identifyByShape(shape: EntityShape): EntityIdentifier {
	const { typeKey, idKey = "id", requires } = shape
	return (value) => {
		if (!value || typeof value !== "object") {
			return undefined
		}

		const record = value as Record<string, unknown>
		const typeName = record[typeKey]
		const id = record[idKey]
		if (typeof typeName !== "string" || typeName.length === 0 || typeof id !== "string" || id.length === 0) {
			return undefined
		}

		return requires && !requires(record) ? undefined : entityKey(typeName, id)
	}
}

/**
 * Collects the identities of every tracked entity reachable from a value, descending through arrays and own
 * enumerable properties into the object graph.
 *
 * Eviction uses this to learn what a live view still holds — a listing array, or a subscribed entity and the
 * entities nested within it. Because the values walked are canonical instances, walking one reaches the whole
 * live graph hanging off it, so an entity kept alive only as a nested reference is still found. The `visited`
 * set is what makes it terminate on the cycles entity references form: an objective points at its sprint,
 * which lists the objective.
 */
export function collectEntityKeys(
	value: unknown,
	into: Set<EntityKey>,
	identify: EntityIdentifier,
	visited: WeakSet<object> = new WeakSet()
): void {
	if (value === null || typeof value !== "object") {
		return
	}

	if (visited.has(value)) {
		return
	}
	visited.add(value)

	const key = identify(value)
	if (key !== undefined) {
		into.add(key)
	}

	if (Array.isArray(value)) {
		for (const item of value) {
			collectEntityKeys(item, into, identify, visited)
		}
		return
	}

	for (const child of Object.values(value as Record<string, unknown>)) {
		collectEntityKeys(child, into, identify, visited)
	}
}
