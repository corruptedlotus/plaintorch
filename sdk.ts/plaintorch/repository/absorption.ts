import { ModelValueConstructor } from "@a11d/api-dotnet"
import type { EntityStore } from "./entityStore"

const modelValueConstructor = new ModelValueConstructor()

/**
 * Builds the `JSON.parse` reviver that turns a response into canonical model instances.
 *
 * A reviver is what makes this correct for whole graphs: it runs bottom-up, so by the time a parent is
 * revived its children are already constructed and already canonical. Constructing only the root — which
 * is what the client did before — left nested models as plain objects and skipped list responses
 * entirely, since a root array carries no runtime type name of its own.
 */
export function createAbsorbingReviver(store: EntityStore): (key: string, value: unknown) => unknown {
	return (_key, value) => {
		return !modelValueConstructor.shallConstruct(value)
			? value
			: store.absorb(modelValueConstructor.construct(value))
	}
}
