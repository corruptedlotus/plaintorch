import { DirectiveTimeframeRecord, TimeframeInclusion } from "@pleiades/sdk"
import { createChild, sleep, SuggestModalBase } from "../../host"
import { core } from ".."
import { IconName } from "../PleiadesIcon"
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise"
import { fuzzyFilter } from "./fuzzy"
import { timeframeSearchText } from "../entities/directiveSearch"

/**
 * A chosen timeframe, or `null` for an explicit none: it clears an executive's affinity or a directive's availability
 * (PEP100 patch 2). An absent value (`undefined`) is not a choice: from a prompt it is a cancel, and in a
 * {@link TimeframeSelect} with `auto` set it stands for Auto (the core assigns the affinity).
 */
export type TimeframeChoice = DirectiveTimeframeRecord | null

/**
 * Picks a timeframe to affine an executive to, mirroring {@link SelectCollegeModal}'s prompt shape (PEP100 patch).
 * The list spans every lunar directive's timeframes, fetched once when the modal is opened, and always leads with a
 * "no affinity" choice so an executive can be un-affined the same way.
 *
 * {@link promptAvailability} is the directive-availability variant (PEP100 patch 2): the same picker narrowed to
 * Availability-mode timeframes, led by a "no availability" choice.
 */
export class SelectTimeframeModal extends SuggestModalBase<TimeframeChoice> {
	/** The pending prompt: resolved with the choice, rejected when the modal closes without one. */
	protected dpe?: DeferredPromiseExecutor<TimeframeChoice | undefined>

	/** The timeframes this opening offers, already narrowed by the prompt that opened it. */
	private timeframes: DirectiveTimeframeRecord[] = []

	/** What the leading `null` row reads as — clearing an affinity, or clearing an availability. */
	private nullText = 'No affinity'

	/**
	 * Picks an executive's affinity among every timeframe. Its first parameter receives the editable's current value
	 * (it is handed straight to `doEdit`), so it carries no options.
	 */
	static prompt = async (_current?: unknown): Promise<TimeframeChoice | undefined> => {
		return await SelectTimeframeModal.openWith(await core.directives.listAllTimeframes(), 'Affine to a timeframe…', 'No affinity')
	}

	/**
	 * Picks a directive's availability (PEP100 patch 2): only {@link TimeframeInclusion.Availability Availability}-mode
	 * timeframes are listed, of any lunar directive, led by a "No availability" choice that resolves `null`. Like
	 * {@link prompt} it fits straight into an editable's `doEdit`.
	 */
	static promptAvailability = async (_current?: unknown): Promise<TimeframeChoice | undefined> => {
		const timeframes = (await core.directives.listAllTimeframes())
			.filter(timeframe => timeframe.autoInclusion === TimeframeInclusion.Availability)
		return await SelectTimeframeModal.openWith(timeframes, 'Pick an availability…', 'No availability')
	}

	/** Opens the picker over the given timeframes; resolves the choice, rejects when closed without one. */
	private static openWith(timeframes: DirectiveTimeframeRecord[], placeholder: string, nullText: string): Promise<TimeframeChoice | undefined> {
		const modal = new SelectTimeframeModal()
		modal.timeframes = timeframes
		modal.nullText = nullText
		modal.setPlaceholder(placeholder)
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	/** The leading `null` row, then the timeframes whose title or directive title fuzzily match the query, best first. */
	override getSuggestions(query: string): TimeframeChoice[] {
		const matches = fuzzyFilter(query, this.timeframes, timeframeSearchText)
		return [null, ...matches]
	}

	/** Draws the `null` row with its clearing label, and a timeframe as its chip, its owning directive chipped beside it. */
	renderSuggestion(choice: TimeframeChoice, el: HTMLElement) {
		if (choice === null) {
			const item = createChild(el, 'p7t-icon-item')
			item.icon = 'lucide:x' as IconName
			item.text = this.nullText
			return
		}

		// The timeframe chip itself, chipped with its owning directive — the same row a timeframe select lists, where
		// two directives may each have a "Morning" — and its full details on hover.
		const item = createChild(el, 'p7t-timeframe-item')
		item.timeframe = choice
		item.chipped = true
	}

	/** Resolves the pending prompt with the choice, `null` included. */
	override onChooseSuggestion(choice: TimeframeChoice) {
		this.dpe?.resolve(choice)
	}

	/** Rejects the pending prompt once the modal has closed; a choice made before closing has already resolved it. */
	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}
}
