import { ModelValueConstructor } from "@a11d/api-dotnet"

/**
 * Runtime type names of the entities the repository system tracks by identity.
 *
 * These are exactly the `PuckNamedEntity` subclasses of the core: everything with a PUCK token for an
 * identifier. Child records such as `Executive`, `Reflective`, `Eventive` and `Attentive` carry numeric
 * keys and are always reached through an owner, so they are absorbed as part of that owner rather than
 * tracked on their own.
 *
 * The list cannot be derived from the `@model` registry: only a few contracts are classes, and a
 * polymorphic entity serializes under its own runtime type name (a fate is `Fate`, not `Objective`).
 */
export const entityTypeNames = [
	"Objective",
	"Fate",
	"Decree",
	"Directive",
	"LunarDirective",
	"PolarisCycle",
	"OnrushSprint",
	"ExecutiveOrder",
	"LorePage"
] as const

/** Runtime type name of a tracked entity. */
export type EntityTypeName = (typeof entityTypeNames)[number]

/** Identity of a tracked entity within the store, in the form `{typeName}:{id}`. */
export type EntityKey = string

const trackedTypeNames: ReadonlySet<string> = new Set(entityTypeNames)

/**
 * Builds the store identity for a type name and identifier.
 */
export function entityKey(typeName: string, id: string): EntityKey {
	return `${typeName}:${id}`
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
 * Works on both raw payloads and constructed model instances, since the type name survives construction.
 */
export function identify(value: unknown): EntityKey | undefined {
	const typeName = typeNameOf(value)
	if (!typeName || !trackedTypeNames.has(typeName)) {
		return undefined
	}

	const id = (value as Record<string, unknown>).id
	return typeof id === "string" && id.length > 0 ? entityKey(typeName, id) : undefined
}

/**
 * Determines whether a value is a tracked entity.
 */
export function isEntity(value: unknown): value is object {
	return identify(value) !== undefined
}
