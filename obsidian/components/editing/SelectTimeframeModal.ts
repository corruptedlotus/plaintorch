import { DirectiveTimeframeRecord } from "@pleiades/sdk"
import { SuggestModal } from "obsidian"
import { getApp } from "."
import { core, resolveMediaIcon } from ".."
import { IconName } from "components/PleiadesIcon"
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise"

/** A chosen timeframe, or `null` to clear affinity outright. */
export type TimeframeChoice = DirectiveTimeframeRecord | null

/**
 * Picks a timeframe to affine an executive to, mirroring {@link SelectCollegeModal}'s prompt shape (PEP100 patch).
 * The list spans every lunar directive's timeframes, fetched once when the modal is opened, and always leads with a
 * "no affinity" choice so an executive can be un-affined the same way.
 */
export class SelectTimeframeModal extends SuggestModal<TimeframeChoice> {
	protected dpe?: DeferredPromiseExecutor<TimeframeChoice | undefined>
	private timeframes: DirectiveTimeframeRecord[] = []

	static prompt = async (_current?: unknown): Promise<TimeframeChoice | undefined> => {
		const modal = new SelectTimeframeModal(getApp())
		modal.timeframes = await core.directives.listAllTimeframes()
		modal.setPlaceholder('Affine to a timeframe…')
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override getSuggestions(query: string): TimeframeChoice[] {
		const q = query.trim().toLowerCase()
		const matches = this.timeframes.filter(timeframe =>
			!q || timeframe.title.toLowerCase().includes(q) || timeframe.directiveTitle.toLowerCase().includes(q))
		return [null, ...matches]
	}

	renderSuggestion(choice: TimeframeChoice, el: HTMLElement) {
		const item = el.createEl('p7t-icon-item')
		if (choice === null) {
			item.icon = 'lucide:x' as IconName
			item.text = 'No affinity'
			return
		}

		item.data = choice
		item.icon = resolveMediaIcon(choice.iconMedia, getApp(), 'lucide:clock') as IconName
		item.text = `${choice.title} · ${choice.directiveTitle}`
	}

	override onChooseSuggestion(choice: TimeframeChoice) {
		this.dpe?.resolve(choice)
	}

	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}
}
