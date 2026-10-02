import type { EntityIdentifier } from "./identity"

/**
 * Turns a wire payload into the model instance a surface works with.
 *
 * The store itself only needs objects it can merge fields into; whether those are plain objects or instances
 * of a class with getters and methods is the consumer's choice, and this is where it is made.
 */
export interface ModelConstructor {
	/** Whether a parsed value is one this constructs. Asked about every node of every response. */
	shallConstruct(value: unknown): boolean
	/**
	 * Builds the model for a payload, or for an existing instance when a draft copies one. May hand the value
	 * back unchanged when it has no class for it.
	 */
	construct(value: object): object
	/**
	 * Whether a type name has a model to be constructed into. A repository refuses to route a type that has
	 * none, because its entities would be tracked as prototype-less plain objects.
	 */
	isRegistered(typeName: string): boolean
}

/**
 * Names the fields of a payload that may be merged onto the canonical instance.
 *
 * A backend rarely serializes "not loaded" differently from "empty" or "cleared", so a response that carries a
 * field is not always speaking for it. This is where a consumer says which of a payload's fields it trusts.
 * `isRoot` is true for the value a response is for, and false for an entity reached inside it.
 */
export type MergeableKeys = (payload: Record<string, unknown>, isRoot: boolean) => readonly string[]

/**
 * Everything a store needs to be told about the domain it holds.
 *
 * Only {@link identify} is required. Left alone, the rest gives a store of plain objects that trusts every
 * field of every response.
 */
export interface EntityPolicy {
	/** What is a tracked entity, and under which identity. */
	readonly identify: EntityIdentifier
	/** How payloads become model instances. Defaults to {@link plainModels}. */
	readonly models?: ModelConstructor
	/** Which fields of a payload merge. Defaults to {@link everyKey}. */
	readonly mergeableKeys?: MergeableKeys
}

/** An {@link EntityPolicy} with its defaults filled in. */
export type ResolvedEntityPolicy = Required<EntityPolicy>

/** Constructs nothing: entities stay the plain objects they were parsed as, and every type name is routable. */
export const plainModels: ModelConstructor = {
	shallConstruct: () => false,
	construct: (value) => value,
	isRegistered: () => true
}

/** Trusts every field a payload carries. */
export const everyKey: MergeableKeys = (payload) => Object.keys(payload)

/** Fills in the defaults of a policy. */
export function resolvePolicy(policy: EntityPolicy): ResolvedEntityPolicy {
	return {
		identify: policy.identify,
		models: policy.models ?? plainModels,
		mergeableKeys: policy.mergeableKeys ?? everyKey
	}
}
