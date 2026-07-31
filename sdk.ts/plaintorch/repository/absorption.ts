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

		const payload = value as Record<string, unknown>
		// Captured before construction: constructing a model widens a sparse payload to the full shape of
		// its class, which would otherwise make an omitted field indistinguishable from a cleared one.
		const payloadKeys = Object.keys(value as object)
		return store.absorb(modelValueConstructor.construct(value), payloadKeys, context)
	}
}

/**
 * Determines whether a collection came back with cycle holes in it.
 *
 * The core serializes with `ReferenceHandler.IgnoreCycles`, which writes `null` wherever an object would
 * recur within its own serialization. A back-reference therefore drags a *truncated* copy of its owner's
 * collection along with it: asking for one objective returns its sprint, and that sprint's `objectives`
 * arrives as `[null]` — the hole being the objective that was asked for.
 *
 * Such a collection is repaired for the instance being constructed and then left out of the payload keys,
 * so it is never merged over a canonical collection that was loaded properly. Both halves matter: without
 * the repair a consumer iterates a hole, and without the omission a fetch of one objective would empty the
 * sprint that every other surface is reading. Nothing in this API sends a meaningful `null` inside an
 * array, so a hole is always this and never data.
 */
function isCycleTruncated(value: unknown): value is unknown[] {
	return Array.isArray(value) && value.some(item => item === null)
}
