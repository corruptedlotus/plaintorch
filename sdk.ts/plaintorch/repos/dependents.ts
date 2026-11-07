import { entityKey, type EntityKey, type EntityTypeName, type DependentTable } from "@corruptedlotus/local-repo"

const key = (typeName: EntityTypeName, id: unknown): EntityKey | undefined =>
	typeof id === "string" && id.length > 0 ? entityKey(typeName, id) : undefined

const asArray = (value: unknown): Record<string, unknown>[] =>
	Array.isArray(value) ? value as Record<string, unknown>[] : []

/**
 * What a change to each PLAINTORCH entity type makes stale, beyond the entity itself.
 *
 * Resolved against the canonical instance rather than a response payload, which is the only place the
 * answer is complete: an objective carries no cycle of its own, but the instance in the store carries the
 * executives that name every cycle holding it.
 *
 * A directive reference names both directive kinds because the foreign key does not say which it points at;
 * naming an identity nothing holds costs nothing, since only an observed one is refetched.
 */
export const plaintorchDependents: DependentTable = {
	Objective: (objective) => [
		key("StellarDirective", objective.directiveId),
		key("LunarDirective", objective.directiveId),
		key("OnrushSprint", objective.onrushSprintId),
		key("Objective", objective.parentIncentiveId),
		key("Fate", objective.parentIncentiveId),
		...asArray(objective.executives).map((executive) => key("PolarisCycle", executive.polarisCycleId))
	],
	Fate: (fate) => [
		key("StellarDirective", fate.directiveId),
		key("LunarDirective", fate.directiveId),
		key("Fate", fate.parentIncentiveId)
	],
	Decree: (decree) => [
		key("StellarDirective", decree.directiveId),
		key("LunarDirective", decree.directiveId),
		...asArray(decree.executives).map((executive) => key("PolarisCycle", executive.polarisCycleId))
	],
	StellarDirective: (directive) => [
		key("StellarDirective", directive.parentDirectiveId),
		key("LunarDirective", directive.parentDirectiveId)
	],
	LunarDirective: (directive) => [
		key("StellarDirective", directive.parentDirectiveId),
		key("LunarDirective", directive.parentDirectiveId)
	],
	OnrushSprint: (sprint) => asArray(sprint.executiveOrders).map((order) => key("ExecutiveOrder", order.id)),
	ExecutiveOrder: (order) => [key("OnrushSprint", order.onrushSprintId)],
	LorePage: (page) => [key("LorePage", page.parentId)]
}
