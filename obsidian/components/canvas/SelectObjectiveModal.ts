import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import type { Objective } from '@pleiades/sdk'
import { SuggestModal } from 'obsidian'
import { core, getApp } from '..'

/**
 * Asks for an objective by title.
 *
 * Searches the core rather than a listing, so an objective that no surface has loaded is still reachable —
 * the point of the picker is to bring something onto the canvas that is not on it yet. An empty query falls
 * back to the full listing, which is what makes the modal useful before anything has been typed.
 */
export class SelectObjectiveModal extends SuggestModal<Objective> {
	private dpe?: DeferredPromiseExecutor<Objective | undefined>
	private excluded: ReadonlySet<string> = new Set()

	/** Prompts for an objective, resolving to nothing when the modal is dismissed. */
	public static prompt(excluded: ReadonlySet<string> = new Set()): Promise<Objective | undefined> {
		const modal = new SelectObjectiveModal(getApp())
		modal.excluded = excluded
		modal.dpe = createDeferredExecutor()
		modal.setPlaceholder('Search objectives…')
		modal.open()
		return new Promise(modal.dpe)
	}

	override async getSuggestions(query: string): Promise<Objective[]> {
		const found = query.trim().length === 0
			? await core.repos.objectiveList.get() ?? []
			: await core.objectives.search(query)
		return found.filter(objective => !this.excluded.has(objective.id))
	}

	override renderSuggestion(objective: Objective, el: HTMLElement) {
		const item = el.createEl('p7t-icon-item')
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
