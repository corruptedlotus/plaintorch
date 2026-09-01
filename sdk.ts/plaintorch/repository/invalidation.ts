import { entityKey, type EntityKey, type EntityTypeName } from "./identity"
import type { EntityStore } from "./entityStore"

/** Names the identities a change to one entity makes stale. */
type DependentResolver = (entity: Record<string, unknown>) => (EntityKey | undefined)[]

const key = (typeName: EntityTypeName, id: unknown): EntityKey | undefined =>
	typeof id === "string" && id.length > 0 ? entityKey(typeName, id) : undefined

const asArray = (value: unknown): Record<string, unknown>[] =>
	Array.isArray(value) ? value as Record<string, unknown>[] : []

/**
 * What a change to each entity type makes stale, beyond the entity itself.
 *
 * Resolved against the canonical instance rather than a response payload, which is the only place the
 * answer is complete: an objective carries no cycle of its own, but the instance in the store carries the
 * executives that name every cycle holding it.
 *
 * Declaring this once is the point. The alternative is every mutation site knowing what else to refresh,
 * which is the arrangement this replaces.
 */
const dependents: Partial<Record<EntityTypeName, DependentResolver>> = {
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
		...asArray(decree.attentives).map((attentive) => key("PolarisCycle", attentive.polarisCycleId))
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

/** Revalidates identities and records that a change made stale. */
export interface InvalidationTarget {
	revalidateEntityIfObserved(key: EntityKey): Promise<void>
	revalidateRecordsIfObserved(): Promise<void>
}

/**
 * Marks what a change made stale and revalidates only what is on screen.
 *
 * Invalidating is not the same as refetching. Everything affected is always marked stale, but a refetch is
 * only worth issuing for something being displayed; anything else is resolved lazily by whoever asks for
 * it next. Dirty identities also settle before any of this runs, so one write that touches four aggregates
 * produces one pass rather than four.
 */
export class InvalidationScheduler {
	private readonly dirtyEntities = new Set<EntityKey>()
	private recordsDirty = false
	private flushing?: Promise<void>
	private scheduled = false

	public constructor(
		private readonly store: EntityStore,
		private readonly target: () => InvalidationTarget
	) { }

	/**
	 * Queues an entity and everything that depends on it.
	 */
	public invalidate(typeName: EntityTypeName, id: string): void {
		const self = entityKey(typeName, id)
		this.dirtyEntities.add(self)

		const entity = this.store.peek<Record<string, unknown>>(self)
		if (entity) {
			for (const dependent of dependents[typeName]?.(entity) ?? []) {
				if (dependent !== undefined) {
					this.dirtyEntities.add(dependent)
				}
			}
		}

		// Every aggregate view is a potential reader of any entity, and revalidating one is gated on it
		// being observed, so naming them individually would buy nothing.
		this.recordsDirty = true
		this.schedule()
	}

	/** Resolves once the queued work has settled. */
	public async settled(): Promise<void> {
		await this.flushing
	}

	private schedule(): void {
		if (this.scheduled) {
			return
		}

		this.scheduled = true
		queueMicrotask(() => {
			this.scheduled = false
			this.flushing = this.flush()
		})
	}

	private async flush(): Promise<void> {
		const entities = [...this.dirtyEntities]
		const records = this.recordsDirty
		this.dirtyEntities.clear()
		this.recordsDirty = false

		for (const entity of entities) {
			this.store.markStale(entity)
		}

		const target = this.target()
		await Promise.all([
			...entities.map(async (entity) => await target.revalidateEntityIfObserved(entity)),
			...(records ? [target.revalidateRecordsIfObserved()] : [])
		])
	}
}
