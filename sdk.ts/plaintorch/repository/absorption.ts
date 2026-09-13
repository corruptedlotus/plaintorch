import { ModelValueConstructor } from "@a11d/api-dotnet"
import type { AbsorptionContext, EntityStore } from "./entityStore"

const modelValueConstructor = new ModelValueConstructor()

/**
 * Builds the `JSON.parse` reviver that turns a response into canonical model instances.
 *
 * A reviver is what makes this correct for whole graphs: it runs bottom-up, so by the time a parent is
 * revived its children are already constructed and already canonical. Constructing only the root — which
 * is what the client did before — left nested models as plain objects and skipped list responses
 * entirely, since a root array carries no runtime type name of its own.
 */
export function createAbsorbingReviver(
	store: EntityStore,
	context?: AbsorptionContext
): (key: string, value: unknown) => unknown {
	return (key, value) => {
		if (!modelValueConstructor.shallConstruct(value)) {
			return value
		}

		// Captured before construction: constructing a model widens a sparse payload to the full shape of
		// its class, which would otherwise make an omitted field indistinguishable from a cleared one.
		const payloadKeys = authoritativeKeys(key, value as Record<string, unknown>)
		return store.absorb(modelValueConstructor.construct(value), payloadKeys, context)
	}
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
 *
 * Root is told from nested by the reviver key: `JSON.parse` calls the reviver for the whole document last,
 * under the empty key. Everything reached under a property name or an array index is nested.
 */
function authoritativeKeys(key: string, payload: Record<string, unknown>): string[] {
	const isRoot = key === ''
	return Object.keys(payload).filter(field => {
		const value = payload[field]

		if (value === null) {
			return !isUnloadedReference(field, payload)
		}

		if (Array.isArray(value)) {
			return isRoot || (value.length > 0 && !value.some(item => item === null))
		}

		return true
	})
}

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
