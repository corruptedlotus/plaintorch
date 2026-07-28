import type { PlaintorchCoreClient } from "../coreClient"
import type { Objective } from "../objectives/models"
import type { Directive } from "../directives/models"
import type { Decree, Fate } from "../declaratives/models"
import type { OnrushSprint } from "../onrush/models"
import type { PolarisCycle } from "../polaris/models"
import type { LorePage } from "../lore/models"
import type { EntityExistence, SystemBriefing } from "../system/models"
import { EntityRepository } from "./entityRepository"
import { DerivedRepository } from "./derivedRepository"
import { InvalidationScheduler, type InvalidationTarget } from "./invalidation"
import { PlaintorchChangeFeed } from "./changeFeed"
import type { EntityKey, EntityTypeName } from "./identity"

/** Key under which the single briefing record is cached. */
export const briefingRecordKey = ""

/**
 * The repository surface of a core client.
 *
 * One repository per tracked entity type, plus derived records for the views the UI subscribes to. The
 * method names mirror the domain SDKs, which the repositories delegate to — the SDK stays the way to talk
 * to the core directly, and this is the way to display something and keep it current.
 */
export class PlaintorchRepositories implements InvalidationTarget {
	/** Queues what a write makes stale and revalidates whatever of it is on screen. */
	public readonly invalidation: InvalidationScheduler

	/**
	 * Carries writes made outside this client — the watcher, the CLI, the scheduler — back to the store.
	 * Not started automatically; the host decides when to listen.
	 */
	public readonly changeFeed: PlaintorchChangeFeed

	public readonly objectives: EntityRepository<Objective>
	public readonly fates: EntityRepository<Fate>
	public readonly decrees: EntityRepository<Decree>
	/** Stellar directives, which the core serializes under the base `Directive` type. */
	public readonly directives: EntityRepository<Directive>
	public readonly lunarDirectives: EntityRepository<Directive>
	public readonly onrush: EntityRepository<OnrushSprint>
	public readonly polaris: EntityRepository<PolarisCycle>
	public readonly lore: EntityRepository<LorePage>

	/** The system briefing, cached under {@link briefingRecordKey}. */
	public readonly briefing: DerivedRepository<SystemBriefing>
	/** Note-to-entity resolutions, keyed by vault-relative path. */
	public readonly noteResolution: DerivedRepository<EntityExistence>
	/** PUCK-to-entity resolutions, keyed by PUCK. */
	public readonly entityResolution: DerivedRepository<EntityExistence>

	private readonly byTypeName: Map<EntityTypeName, EntityRepository<never>>

	public constructor(private readonly client: PlaintorchCoreClient) {
		const store = client.store
		// Resolved lazily: the scheduler reaches back into the repositories it is handed to.
		this.invalidation = new InvalidationScheduler(store, () => this)
		const options = { invalidation: this.invalidation }

		this.objectives = new EntityRepository(store, "Objective", (id) => client.objectives.get(id), options)
		this.fates = new EntityRepository(store, "Fate", (id) => client.declaratives.getFate(id), options)
		this.decrees = new EntityRepository(store, "Decree", (id) => client.declaratives.getDecree(id), options)
		this.directives = new EntityRepository(store, "Directive", (id) => client.directives.get(id), options)
		this.lunarDirectives = new EntityRepository(store, "LunarDirective", (id) => client.directives.get(id), options)
		this.onrush = new EntityRepository(store, "OnrushSprint", (id) => client.onrush.get(id), options)
		this.polaris = new EntityRepository(store, "PolarisCycle", (id) => client.polaris.get(id), options)
		this.lore = new EntityRepository(store, "LorePage", (id) => client.lore.get(id), options)

		this.briefing = new DerivedRepository(async () => await client.system.getBriefing())
		this.noteResolution = new DerivedRepository(async (path) => await client.system.resolveNote(path))
		this.entityResolution = new DerivedRepository(async (puck) => await client.system.resolveEntity(puck))

		this.byTypeName = new Map<EntityTypeName, EntityRepository<never>>([
			["Objective", this.objectives as EntityRepository<never>],
			["Fate", this.fates as EntityRepository<never>],
			["Decree", this.decrees as EntityRepository<never>],
			["Directive", this.directives as EntityRepository<never>],
			["LunarDirective", this.lunarDirectives as EntityRepository<never>],
			["OnrushSprint", this.onrush as EntityRepository<never>],
			["PolarisCycle", this.polaris as EntityRepository<never>],
			["LorePage", this.lore as EntityRepository<never>]
		])

		this.changeFeed = new PlaintorchChangeFeed(client, this)
	}

	/**
	 * Resolves the repository responsible for a runtime type name.
	 *
	 * Lets a generic surface — a banner that only knows which kind of note it is rendering — resolve and
	 * observe an entity without a switch over every type.
	 */
	public forTypeName<T extends object>(typeName: string | undefined): EntityRepository<T> | undefined {
		return typeName === undefined
			? undefined
			: this.byTypeName.get(typeName as EntityTypeName) as EntityRepository<T> | undefined
	}

	/**
	 * Revalidates every derived record that something is observing.
	 *
	 * Used as the coarse fallback when a promptness signal is unavailable — on surface activation, or when
	 * a change feed reconnects after missing events.
	 */
	public async revalidateObservedRecords(): Promise<void> {
		await Promise.all([
			this.briefing.revalidateObserved(),
			this.noteResolution.revalidateObserved(),
			this.entityResolution.revalidateObserved()
		])
	}

	/** @inheritdoc */
	public async revalidateRecordsIfObserved(): Promise<void> {
		await this.revalidateObservedRecords()
	}

	/**
	 * @inheritdoc
	 *
	 * Routed by type name so the scheduler can name a dependency of any kind without knowing which
	 * repository serves it.
	 */
	public async revalidateEntityIfObserved(key: EntityKey): Promise<void> {
		const separator = key.indexOf(":")
		const typeName = key.slice(0, separator)
		const id = key.slice(separator + 1)
		await this.forTypeName(typeName)?.revalidateIfObserved(id)
	}

	/**
	 * Revalidates everything currently on screen — entities and records alike.
	 *
	 * The coarse recovery path, for when promptness cannot be relied on: a change feed reconnecting after
	 * missing events, or a host waking a surface back up.
	 */
	public async revalidateObserved(): Promise<void> {
		await Promise.all([
			...[...this.byTypeName.values()].map(async (repository) => await repository.revalidateObserved()),
			this.revalidateObservedRecords()
		])
	}

	/** Marks everything as needing revalidation, without fetching anything. */
	public invalidateAll(): void {
		this.client.store.markAllStale()
		this.briefing.invalidate()
		this.noteResolution.invalidate()
		this.entityResolution.invalidate()
	}
}
