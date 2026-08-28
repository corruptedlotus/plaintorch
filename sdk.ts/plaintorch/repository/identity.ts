import { ModelValueConstructor } from "@a11d/api-dotnet"

/**
 * Runtime type name of a tracked entity, as the core stamps it on the wire.
 *
 * Deliberately not a closed union. It was one, listing the type names by hand, and the list drifted: the
 * core's concrete stellar directive is `StellarDirective`, the list said `Directive`, and every directive
 * silently stopped being tracked — an entity that is never absorbed has no canonical instance, so nothing
 * observing it ever hears anything. Which names exist belongs to the core, so recognizing an entity is now
 * structural (see {@link identify}) and only *routing* a name to a repository is declared.
 */
export type EntityTypeName = string

/** Identity of a tracked entity within the store, in the form `{typeName}:{id}`. */
export type EntityKey = string

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

/**
 * Reads the runtime type name the core stamps onto every serialized object.
 */
export function typeNameOf(value: unknown): string | undefined {
	if (!value || typeof value !== "object") {
		return undefined
	}

	const typeName = (value as Record<string, unknown>)[ModelValueConstructor.typeNameKey]
	return typeof typeName === "string" ? typeName : undefined
}

/**
 * Resolves the store identity of a value, or `undefined` when it is not a tracked entity.
 *
 * An entity is recognized by shape rather than by name: the core's `IPuckNamedEntity` contract is a PUCK
 * token and a title, so a runtime type name plus a non-empty string `id` plus a string `title` is exactly
 * what an entity looks like on the wire, whatever it happens to be called.
 *
 * Both halves of the shape test carry weight. The `id` must be a string because a child record like a
 * dependency edge keys on a number, and the `title` must be present because a value object can carry a
 * string `id` that is a *reference* to another entity — a dependency endpoint does, and tracking those
 * would merge two unrelated references that happen to point at the same entity into one instance.
 *
 * Works on raw payloads and constructed model instances alike, since the type name survives construction.
 */
export function identify(value: unknown): EntityKey | undefined {
	const typeName = typeNameOf(value)
	if (!typeName) {
		return undefined
	}

	const record = value as Record<string, unknown>
	const id = record.id
	if (typeof id !== "string" || id.length === 0 || typeof record.title !== "string") {
		return undefined
	}

	return entityKey(typeName, id)
}

/**
 * Determines whether a value is a tracked entity.
 */
export function isEntity(value: unknown): value is object {
	return identify(value) !== undefined
}
