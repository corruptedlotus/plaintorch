import { entityKey, type EntityTypeName } from "./identity"
import type { EntityStore } from "./entityStore"
import type { InvalidationScheduler } from "./invalidation"

/**
 * Whether a write result means the core accepted it.
 *
 * The client reports a failed request by returning nothing rather than throwing, so an absent result — undefined,
 * null, or false — is a rejection and not merely an operation with no return value.
 */
export function isSuccessfulMutation(result: unknown): boolean {
	return result !== undefined && result !== null && result !== false
}

export interface RunWriteOptions<R> {
	/** Field values to restore if the write is rejected — captured before the edit was applied. */
	rollbackTo?: Record<string, unknown>
	/** Whether a result counts as success. Defaults to {@link isSuccessfulMutation}. */
	succeeded?: (result: R) => boolean
}

/**
 * The one write cycle every path shares: hold the identity, run the operation, then invalidate on success or
 * roll back on rejection.
 *
 * Holding the identity — {@link EntityStore.beginWrite} — lets a repository skip a revalidation while the write
 * runs and notes the local change at both ends, so a read taken before or during the write is superseded rather
 * than allowed to revert it. Correctness no longer depends on the hold (the revision ordering discards a racing
 * response regardless); the hold only spares a wasteful refetch. The imperative `mutate`, the reference's
 * immediate `commit`, and a draft's `commit` all run through here, so the guard lives once, in the store.
 */
export async function runWrite<R>(
	store: EntityStore,
	invalidation: InvalidationScheduler | undefined,
	typeName: EntityTypeName,
	id: string,
	operation: () => Promise<R>,
	options: RunWriteOptions<R> = {}
): Promise<{ result: R, ok: boolean }> {
	const key = entityKey(typeName, id)
	store.beginWrite(key)
	try {
		const result = await operation()
		const ok = (options.succeeded ?? isSuccessfulMutation)(result)
		if (ok) {
			invalidation?.invalidate(typeName, id)
		}
		else if (options.rollbackTo) {
			store.restore(key, options.rollbackTo)
		}

		return { result, ok }
	}
	catch (error) {
		if (options.rollbackTo) {
			store.restore(key, options.rollbackTo)
		}

		throw error
	}
	finally {
		store.endWrite(key)
	}
}
