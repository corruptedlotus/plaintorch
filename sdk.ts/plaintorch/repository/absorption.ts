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
	return (_key, value) => {
		if (!modelValueConstructor.shallConstruct(value)) {
			return value
		}

		// Captured before construction: constructing a model widens a sparse payload to the full shape of
		// its class, which would otherwise make an omitted field indistinguishable from a cleared one.
		const payloadKeys = Object.keys(value as object)
		return store.absorb(modelValueConstructor.construct(value), payloadKeys, context)
	}
}
