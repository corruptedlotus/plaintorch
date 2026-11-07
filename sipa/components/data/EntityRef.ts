import type { ReactiveElement } from '@a11d/lit'
import {
	EntityRef as LocalEntityRef, EntityWatch as LocalEntityWatch, QueryRef, type EntityRefSource
} from '@corruptedlotus/local-repo/lit'
import type { EntityRepository } from '@pleiades/sdk'
import { getCore } from './coreProvider'
import { toast } from '../../host'

export type {
	EntityRefSource, EntityFieldPersist, EntityFieldWrite, EntityPersistMap, EntityCommitReaction, EntityRefOptions
} from '@corruptedlotus/local-repo/lit'
export { QueryRef }

/**
 * The repository package's {@link LocalEntityRef}, with what PLAINTORCH adds: a rejected write is reported through
 * the platform host's toast. Everything else — resolve, observe, re-render, the edit cycle, the data states — is the
 * package's; see `@corruptedlotus/local-repo/lit`.
 */
export class EntityRef<T extends object> extends LocalEntityRef<T> {
	public constructor(host: ReactiveElement, repository: () => EntityRepository<T> | undefined, source: EntityRefSource) {
		super(host, repository, source, { rejected: () => toast('PLAINTORCH could not save that change.', 'error') })
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
