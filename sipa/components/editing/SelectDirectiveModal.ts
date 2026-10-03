import { Directive } from "@pleiades/sdk"
import { createChild, sleep, SuggestModalBase } from "../../host"
import { core } from ".."
import { directiveAncestors } from "../entities/directiveLineage"
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise"
import { fuzzyFilter } from "./fuzzy"

/**
 * A chosen directive, or `null` for an explicit none — the top of the hierarchy for a directive, the world for an
 * incentive. An absent value (`undefined`) is not a choice: from a prompt it is a cancel.
 */
export type DirectiveChoice = Directive | null

/** What one opening of the directive picker offers. */
export interface DirectivePromptOptions {
	/** The query input's placeholder. */
	placeholder?: string
	/** What the leading `null` row reads as. Without it no `null` row is offered. */
	nullText?: string
	/** Which directives are offered; every directive by default. The listing is filtered before it is searched. */
	allow?: (directive: Directive) => boolean
}

/**
 * Picks a directive among every directive in the vault, mirroring {@link SelectTimeframeModal}'s prompt shape: the
 * listing is read once when the picker opens, searched fuzzily by title, codename and ancestry, and each row is the
 * directive's chip with its ancestors chipped beside it — the way a timeframe row chips its owning directive — so two
 * directives of one title read apart by where they sit.
 *
 * It is the picker behind the hierarchy editable and the "Move to…" row of an entity's menu, which narrow it through
 * {@link DirectivePromptOptions.allow} to the directives an entity may actually go under, and lead it with a `null`
 * row — the top level, or the world — when the entity is not there already.
 */
export class SelectDirectiveModal extends SuggestModalBase<DirectiveChoice> {
	/** The pending prompt: resolved with the choice, rejected when the modal closes without one. */
	protected dpe?: DeferredPromiseExecutor<DirectiveChoice | undefined>

	/** The directives this opening offers, already narrowed by the prompt that opened it. */
	private directives: Directive[] = []

	/** Every directive by id, for walking a row's ancestry — the offered ones and those filtered out alike. */
	private byId = new Map<string, Directive>()

	/** What the leading `null` row reads as; none when no such row is offered. */
	private nullText?: string

	/** Opens the picker over the directive listing; resolves the choice, rejects when closed without one. */
	static prompt = async (options: DirectivePromptOptions = {}): Promise<DirectiveChoice | undefined> => {
		const all = await core.repos.directiveList.get() ?? []
		const modal = new SelectDirectiveModal()
		modal.byId = new Map(all.map(directive => [directive.id, directive]))
		modal.directives = options.allow ? all.filter(options.allow) : [...all]
		modal.nullText = options.nullText
		modal.setPlaceholder(options.placeholder ?? 'Pick a directive…')
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	/** The text a directive is searched by: its title and codename, then every ancestor's. */
	private searchTextOf(directive: Directive): string {
		return [directive, ...directiveAncestors(directive, this.byId)]
			.map(step => `${step.title} ${step.codename ?? ''}`)
			.join(' ')
	}

	/** The leading `null` row when offered, then the directives fuzzily matching the query, best first. */
	override getSuggestions(query: string): DirectiveChoice[] {
		const matches = fuzzyFilter(query, this.directives, directive => this.searchTextOf(directive))
		return this.nullText === undefined ? matches : [null, ...matches]
	}

	/** Draws the `null` row with its label, and a directive as its chip with its ancestors chipped beside it. */
	renderSuggestion(choice: DirectiveChoice, el: HTMLElement) {
		if (choice === null) {
			const item = createChild(el, 'p7t-icon-item')
			item.icon = 'lucide:x'
			item.text = this.nullText ?? 'None'
			return
		}

		const item = createChild(el, 'p7t-directive-item')
		item.directive = choice
		item.chipped = true
	}

	/** Resolves the pending prompt with the choice, `null` included. */
	override onChooseSuggestion(choice: DirectiveChoice) {
		this.dpe?.resolve(choice)
	}

	/** Rejects the pending prompt once the modal has closed; a choice made before closing has already resolved it. */
	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}
}
