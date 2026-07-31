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
 * The root of a response speaks for the entity it fetched, collections and all — a directly fetched onrush's
 * `checkpoints: []` is a genuine emptying and must apply. A *nested* entity does not: it is a back-reference
 * dragged in by an `Include`, and its navigations were not loaded, so the core serializes an unloaded
 * collection as an empty array and a cycle back-reference as a `[null]` hole (`ReferenceHandler.IgnoreCycles`
 * writes `null` where an object would recur). Either, merged, would wipe the collection a direct fetch had
 * populated — which is how a checkpoint vanished the moment an objective beside it was refetched, since the
 * objective carries the sprint as a back-reference. So a nested entity withholds its empty and holed arrays,
 * keeping only a genuinely populated one, while its scalar and single-reference fields merge as before.
 *
 * Root is told from nested by the reviver key: `JSON.parse` calls the reviver for the whole document last,
 * under the empty key. Everything reached under a property name or an array index is nested.
 */
function authoritativeKeys(key: string, payload: Record<string, unknown>): string[] {
	const keys = Object.keys(payload)
	if (key === '') {
		return keys
	}

	return keys.filter(field => {
		const value = payload[field]
		return !Array.isArray(value) || (value.length > 0 && !value.some(item => item === null))
	})
}
