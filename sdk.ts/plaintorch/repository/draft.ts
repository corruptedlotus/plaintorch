import { ModelValueConstructor } from "@a11d/api-dotnet"
import type { EntityStore } from "./entityStore"
import type { EntityKey, EntityTypeName } from "./identity"
import type { InvalidationScheduler } from "./invalidation"
import { isEquivalent } from "./equivalence"

const modelValueConstructor = new ModelValueConstructor()

/** Mirrors `isSuccessfulMutation`: an absent result — undefined, null, or false — is a rejection. */
function isAccepted(result: unknown): boolean {
	return result !== undefined && result !== null && result !== false
}

/** Persists a draft's committed changes. Receives the changed fields and the reconciled canonical entity. */
export type DraftPersist<T> = (patch: Partial<T>, entity: T) => Promise<unknown>

/**
 * An isolated, mutable working copy of one entity — the fork-and-commit editing mode.
 *
 * Fork takes a prototype-correct copy of the entity plus a snapshot of its fields. The copy is edited freely
 * and nothing reaches the shared store until {@link commit}, which diffs the copy against the fork-time
 * baseline and sends only the fields that changed; {@link cancel} throws the copy away. This is what a
 * multi-field editor with a cancel, or a background recomputation, uses instead of the immediate write a
 * two-way binding performs.
 *
 * Drafts are shallow and field-level by policy: the copy shares nested arrays and objects with the canonical
 * instance, so collections must be edited through explicit API actions, never by mutating a draft's array.
 */
export class EntityDraft<T extends object> {
	/** The mutable working copy. Edit its fields; nothing propagates until {@link commit}. */
	public readonly value: T
	/**
	 * Field values at fork time. The diff baseline: commit sends only fields that differ from this, so a
	 * concurrent edit to a field the draft did not touch survives.
	 */
	private readonly baseline: Record<string, unknown>
	private spent = false

	public constructor(
		private readonly store: EntityStore,
		private readonly key: EntityKey,
		private readonly typeName: EntityTypeName,
		private readonly id: string,
		canonical: T,
		private readonly invalidation?: InvalidationScheduler
	) {
		// Copied through the same construction absorption uses, so the draft is a real instance of its class
		// (getters, methods, prototype) rather than a plain spread that would drop them.
		this.value = modelValueConstructor.construct(canonical) as T
		this.baseline = { ...(canonical as Record<string, unknown>) }
	}

	/** The fields whose value differs from the fork-time baseline. */
	public diff(): Partial<T> {
		const patch: Record<string, unknown> = {}
		const value = this.value as Record<string, unknown>
		for (const field of Object.keys(value)) {
			if (!isEquivalent(value[field], this.baseline[field])) {
				patch[field] = value[field]
			}
		}

		return patch as Partial<T>
	}

	/** Whether the draft holds any uncommitted change. */
	public get dirty(): boolean {
		return Object.keys(this.diff()).length > 0
	}

	/**
	 * Applies the draft's changes to the canonical instance, sends them, and rolls back on rejection.
	 *
	 * The diff is taken against the fork-time baseline, not the live instance, so only the fields the draft
	 * actually changed are written and sent — a concurrent edit to another field is left intact. A rejected
	 * write restores the pre-commit values. A no-op when nothing changed, and spent after the first call.
	 */
	public async commit(persist: DraftPersist<T>): Promise<boolean> {
		if (this.spent) {
			return false
		}
		this.spent = true

		const patch = this.diff()
		if (Object.keys(patch).length === 0) {
			return true
		}

		const snapshot = this.store.snapshot(this.key)
		this.store.patch(this.key, patch as Record<string, unknown>)
		const entity = this.store.peek<T>(this.key) ?? this.value

		const result = await persist(patch, entity)
		if (!isAccepted(result)) {
			if (snapshot) {
				this.store.restore(this.key, snapshot)
			}

			return false
		}

		this.invalidation?.invalidate(this.typeName, this.id)
		return true
	}

	/** Discards the draft. Nothing was applied to the store, so this only marks it spent. */
	public cancel(): void {
		this.spent = true
	}
}
