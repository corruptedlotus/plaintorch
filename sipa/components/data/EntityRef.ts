import type { ReactiveElement } from '@a11d/lit'
import {
	EntityRef as LocalEntityRef, EntityWatch as LocalEntityWatch, QueryRef, type EntityFieldPersist, type EntityRefSource
} from '@corruptedlotus/local-repo/lit'
import type { EntityRepository, StateEnforcementProblem } from '@pleiades/sdk'
import { getCore } from './coreProvider'
import { toast } from '../../host'

export type {
	EntityRefSource, EntityFieldPersist, EntityFieldWrite, EntityPersistMap, EntityCommitReaction, EntityRefOptions
} from '@corruptedlotus/local-repo/lit'
export { QueryRef }

/** How {@link EntityRef.awaitCommit} sends a change. */
export interface EntityCommitOptions {
	/** Sends it forcing the compensations the core proposed: the user's confirmation of a held-back change (PEP103). */
	readonly force?: boolean
}

/** What became of a change {@link EntityRef.awaitCommit} sent, once the core gave its final answer. */
export interface EntityCommitOutcome {
	/** Whether the core accepted it. */
	readonly saved: boolean
	/** The core's state enforcement answer (PEP103), when it held the change back for confirmation or refused it. */
	readonly enforcement?: StateEnforcementProblem
}

/**
 * The repository package's {@link LocalEntityRef}, with what PLAINTORCH adds: a rejected write is reported through
 * the platform host's toast, and {@link awaitCommit} answers the core's state enforcement (PEP103) for one change.
 * Everything else — resolve, observe, re-render, the edit cycle, the data states — is the package's; see
 * `@corruptedlotus/local-repo/lit`.
 */
export class EntityRef<T extends object> extends LocalEntityRef<T> {
	public constructor(
		host: ReactiveElement,
		private readonly repositoryOf: () => EntityRepository<T> | undefined,
		private readonly idOf: EntityRefSource
	) {
		super(host, repositoryOf, idOf, { rejected: () => toast('PLAINTORCH could not save that change.', 'error') })
	}

	/**
	 * Sends a change and awaits the core's final answer for this one commit, for a surface that asked for the change itself
	 * and shows what became of it — the state bubble. A state enforcement answer (PEP103) comes back with the outcome rather
	 * than going to the client's global hook, which would put up a prompt of its own, and no failure toast is raised: the
	 * caller shows the outcome where the change was asked for.
	 *
	 * The change is one the caller has not applied in advance, so there is nothing to roll back; it runs under the same
	 * write guard and invalidation as {@link commit}. An edit a binding has already written into the entity goes through
	 * {@link commit}, which undoes it on a refusal.
	 */
	public async awaitCommit(send: EntityFieldPersist<T>, options: EntityCommitOptions = {}): Promise<EntityCommitOutcome> {
		const repository = this.repositoryOf()
		const id = this.idOf()
		const entity = this.value
		if (!repository || !id || !entity) {
			return { saved: false }
		}

		repository.touch(id)
		let enforcement: StateEnforcementProblem | undefined
		// The commit sends synchronously, which is what puts its request inside the enforcement scope.
		const saved = await getCore().withStateEnforcement(
			{ force: options.force, answer: problem => enforcement = problem },
			() => repository.commit(id, async () => await send(entity)))
		return { saved, enforcement }
	}
}

/**
 * The repository package's {@link LocalEntityWatch} over the installed core's store, so a component observing an
 * entity it was handed names only the entity, not where it is kept.
 */
export class EntityWatch extends LocalEntityWatch {
	public constructor(host: ReactiveElement, source: () => unknown) {
		super(host, () => getCore().store, source)
	}
}
