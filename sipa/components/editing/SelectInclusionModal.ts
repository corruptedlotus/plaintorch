import { TimeframeInclusion } from "@pleiades/sdk"
import { createChild, sleep, SuggestModalBase } from "../../host"
import { inclusionDescriptors, inclusionDescriptorOf } from "../entities/inclusionDescriptors"
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise"

/**
 * Picks a timeframe's auto-inclusion mode — None, College or Availability (PEP100 patch 2), mirroring
 * {@link SelectCollegeModal}'s prompt shape. {@link prompt} fits straight into an editable's `doEdit`: it resolves the
 * chosen mode and rejects when the modal closes without a choice.
 */
export class SelectInclusionModal extends SuggestModalBase<TimeframeInclusion> {
	/** The pending prompt: resolved with the chosen mode, rejected when the modal closes without one. */
	protected dpe?: DeferredPromiseExecutor<TimeframeInclusion | undefined>

	/**
	 * Opens the mode picker (PEP100 patch 2). Its parameter receives the editable's current value, since it is handed
	 * straight to `doEdit`, and is unused. Resolves the chosen {@link TimeframeInclusion}; rejects when the modal
	 * closes without a choice, so the editable keeps its value.
	 */
	static prompt = (_currentValue?: TimeframeInclusion): Promise<TimeframeInclusion | undefined> => {
		const modal = new SelectInclusionModal()
		modal.setPlaceholder('Auto-include by…')
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	/** Every inclusion mode, in descriptor order; the list is short, so the query does not filter it. */
	override getSuggestions(_query: string) {
		return Object.values(inclusionDescriptors).map(descriptor => descriptor.value)
	}

	/** Draws a mode as its descriptor's glyph and full label. */
	renderSuggestion(inclusion: TimeframeInclusion, el: HTMLElement) {
		const item = createChild(el, 'p7t-icon-item')
		const descriptor = inclusionDescriptorOf(inclusion)
		item.data = inclusion
		item.icon = descriptor.icon
		item.text = descriptor.fullName
	}

	/** Rejects the pending prompt once the modal has closed; a choice made before closing has already resolved it. */
	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}

	/** Resolves the pending prompt with the chosen mode. */
	override async onChooseSuggestion(item: TimeframeInclusion, _: MouseEvent | KeyboardEvent) {
		this.dpe?.resolve(item)
	}
}
