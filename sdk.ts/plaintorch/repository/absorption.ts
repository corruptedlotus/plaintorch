import type { AbsorptionContext, EntityStore } from "./entityStore"

/**
 * Builds the `JSON.parse` reviver that turns a response into canonical model instances.
 *
 * A reviver is what makes this correct for whole graphs: it runs bottom-up, so by the time a parent is
 * revived its children are already constructed and already canonical. Constructing only the root — which
 * is what the client did before — left nested models as plain objects and skipped list responses
 * entirely, since a root array carries no runtime type name of its own.
 *
 * What is constructed, what is tracked, and which of a payload's fields are trusted all come from the store's
 * policy. The one thing decided here is which value is the response's root: `JSON.parse` calls the reviver for
 * the whole document last, under the empty key, and everything reached under a property name or an array
 * index is nested.
 */
export function createAbsorbingReviver(
	store: EntityStore,
	context?: AbsorptionContext
): (key: string, value: unknown) => unknown {
	const { models, mergeableKeys } = store.policy
	return (key, value) => {
		const constructs = models.shallConstruct(value)
		if (!constructs && store.identify(value) === undefined) {
			return value
		}

		// Captured before construction: constructing a model widens a sparse payload to the full shape of
		// its class, which would otherwise make an omitted field indistinguishable from a cleared one.
		const payload = value as Record<string, unknown>
		const payloadKeys = mergeableKeys(payload, key === "")
		return store.absorb(constructs ? models.construct(payload) : payload, payloadKeys, context)
	}
}
