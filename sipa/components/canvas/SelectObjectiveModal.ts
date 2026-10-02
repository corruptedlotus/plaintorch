import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import type { Objective } from '@pleiades/sdk'
import { createChild, sleep, SuggestModalBase } from '../../host'
import { core } from '..'
import { fuzzyFilter } from '../editing/fuzzy'

/**
 * Asks for an objective by title.
 *
 * Matches against the core's full objective listing rather than what any surface has loaded, so an objective
 * that is not on the canvas yet is still reachable — the point of the picker is to bring it there. The listing
 * is matched by {@link fuzzyFilter}; an empty query offers all of it, so the modal is useful before typing.
 */
export class SelectObjectiveModal extends SuggestModalBase<Objective> {
	private dpe?: DeferredPromiseExecutor<Objective | undefined>
	private excluded: ReadonlySet<string> = new Set()

	/** Prompts for an objective, resolving to nothing when the modal is dismissed. */
	public static prompt(excluded: ReadonlySet<string> = new Set()): Promise<Objective | undefined> {
		const modal = new SelectObjectiveModal()
		modal.excluded = excluded
		modal.dpe = createDeferredExecutor()
		modal.setPlaceholder('Search objectives…')
		modal.open()
		return new Promise(modal.dpe)
	}

	override async getSuggestions(query: string): Promise<Objective[]> {
		const offered = (await core.repos.objectiveList.get() ?? []).filter(objective => !this.excluded.has(objective.id))
		return fuzzyFilter(query, offered, objective => objective.title)
	}

	override renderSuggestion(objective: Objective, el: HTMLElement) {
		const item = createChild(el, 'p7t-icon-item')
		item.data = objective
		item.icon = 'objective'
		item.text = objective.title
	}

	override async onClose() {
		// The suggestion handler runs after the close, so resolving has to lose the race deliberately.
		await sleep(500)
		this.dpe?.resolve(undefined)
	}

	override onChooseSuggestion(objective: Objective, _: MouseEvent | KeyboardEvent) {
		this.dpe?.resolve(objective)
	}
}
