import { ModelValueConstructor } from "@a11d/api-dotnet"
import { identifyByShape, type EntityIdentifier } from "./repository/identity"
import type { EntityPolicy, MergeableKeys, ModelConstructor } from "./repository/policy"

// What the repository system has to be told about PLAINTORCH: what an entity looks like on the core's wire,
// how a payload becomes one of the SDK's model classes, and which fields of a response the core means.

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
export const identify: EntityIdentifier = identifyByShape({
	typeKey: ModelValueConstructor.typeNameKey,
	requires: (value) => typeof value.title === "string"
})

/**
 * Determines whether a value is a tracked entity.
 */
export function isEntity(value: unknown): value is object {
	return identify(value) !== undefined
}

const modelValueConstructor = new ModelValueConstructor()

/**
 * Constructs the SDK's model classes through `@a11d/api-dotnet`'s registry, which the `@model` decorators
 * fill as their modules load.
 */
export const plaintorchModels: ModelConstructor = {
	shallConstruct: (value) => modelValueConstructor.shallConstruct(value),
	construct: (value) => modelValueConstructor.construct(value),
	isRegistered: (typeName) => ModelValueConstructor.modelConstructorsByTypeName.has(typeName)
}

/**
 * The fields a payload is allowed to merge onto the canonical instance.
 *
 * Two kinds of field are withheld, both because a lean or nested response carries them not as "this is
 * cleared" but as "this was not loaded":
 *
 * **Unloaded collections.** The root of a response speaks for the entity it fetched — a directly fetched
 * onrush's `checkpoints: []` is a genuine emptying and must apply. A *nested* entity does not: it is a
 * back-reference dragged in by an `Include`, and its navigations were not loaded, so the core serializes an
 * unloaded collection as an empty array and a cycle back-reference as a `[null]` hole
 * (`ReferenceHandler.IgnoreCycles` writes `null` where an object would recur). Either, merged, would wipe the
 * collection a direct fetch had populated — which is how a checkpoint vanished the moment an objective beside
 * it was refetched. So a nested entity withholds its empty and holed arrays, keeping only a populated one.
 *
 * **Unloaded single references.** A navigation like `directive` serializes as `null` both when it is genuinely
 * absent and when it was simply not loaded — a lean write (`objectives.update` returning a base objective) or a
 * summary (`directives.get`) returns the entity with the nav `null` but its foreign key still set. Merged, that
 * `null` wipes the nav a listing populated, while the `directiveId` beside it survives — the exact asymmetry a
 * field trial hit (a banner's directive vanished on save while the tree, which reads the id, held). A genuine
 * clear nulls the foreign key too, so the two are told apart by coherence: a null single reference is withheld
 * unless the same payload also nulls its `<field>Id`. This applies to root and nested alike, since the lean
 * write returns the entity as the root. A plain nullable scalar (no `<field>Id` companion, like a `due` date)
 * is not a reference and always applies.
 */
export const plaintorchMergeableKeys: MergeableKeys = (payload, isRoot) =>
	Object.keys(payload).filter(field => {
		const value = payload[field]

		if (value === null) {
			return !isUnloadedReference(field, payload)
		}

		if (Array.isArray(value)) {
			return isRoot || (value.length > 0 && !value.some(item => item === null))
		}

		return true
	})

/**
 * Whether a null field is a navigation reference that was not loaded, rather than one genuinely cleared.
 *
 * Its foreign key tells them apart: an id still pointing somewhere beside a null nav is incoherent — the nav
 * was not loaded. A genuine clear nulls the id too. A field with no `<field>Id` companion in the payload is a
 * plain scalar, not a reference, and is never withheld on this account.
 */
function isUnloadedReference(field: string, payload: Record<string, unknown>): boolean {
	const foreignKeyField = `${field}Id`
	return foreignKeyField in payload && payload[foreignKeyField] !== null
}

/** The policy every PLAINTORCH client builds its entity store with. */
export const plaintorchEntityPolicy: EntityPolicy = {
	identify,
	models: plaintorchModels,
	mergeableKeys: plaintorchMergeableKeys
}
