import type { SuggestLayout, SuggestShell } from './dialogs'
import { getHost } from './provider'

/**
 * Base of the UI's searching pickers, shown by whatever application hosts the UI (see `DialogHost`).
 *
 * A picker says what a query finds ({@link getSuggestions}), how a suggestion looks ({@link renderSuggestion}) and
 * what choosing one does ({@link onChooseSuggestion}); the host draws the input and the list, walks it with the
 * keyboard and reports the choice. {@link layout} asks for a grid of tiles instead of a list of rows.
 *
 * Choosing closes the picker and then calls {@link onChooseSuggestion} — the order Obsidian's pickers had, so a
 * picker's {@link onClose} runs before its choice arrives. A picker that must stay open on a choice overrides
 * {@link selectSuggestion}.
 */
export abstract class SuggestModalBase<T> {
	/** Rows or tiles. Set before opening. */
	layout: SuggestLayout = 'list'

	private placeholder = ''
	private shell?: SuggestShell

	/** The query input's placeholder. Set before opening. */
	setPlaceholder(text: string): void {
		this.placeholder = text
	}

	/** The suggestions for a query: `''` when the picker opens, then on every edit. */
	abstract getSuggestions(query: string): T[] | Promise<T[]>

	/** Draws one suggestion into the row (or tile) the host provides. */
	abstract renderSuggestion(value: T, el: HTMLElement): void

	/** A suggestion was chosen (after the picker closed, unless {@link selectSuggestion} is overridden). */
	abstract onChooseSuggestion(item: T, evt: MouseEvent | KeyboardEvent): void | Promise<void>

	/** Handles a choice: by default closes the picker, then {@link onChooseSuggestion}. */
	selectSuggestion(value: T, evt: MouseEvent | KeyboardEvent): void {
		this.close()
		void this.onChooseSuggestion(value, evt)
	}

	/** Replaces the query, as if typed, and searches again. Only while open. */
	setQuery(query: string): void {
		this.shell?.setQuery(query)
	}

	/** Shows the picker. Opening one that is already open does nothing. */
	open(): void {
		if (this.shell) {
			return
		}

		this.shell = getHost().dialogs.createSuggest<T>({
			placeholder: this.placeholder,
			layout: this.layout,
			getSuggestions: query => this.getSuggestions(query),
			renderSuggestion: (value, el) => this.renderSuggestion(value, el),
			selectSuggestion: (value, evt) => this.selectSuggestion(value, evt),
			handleOpen: () => void this.onOpen(),
			handleClose: () => {
				this.shell = undefined
				void this.onClose()
			},
		})
		this.shell.open()
	}

	/** Dismisses the picker; {@link onClose} follows. */
	close(): void {
		this.shell?.close()
	}

	onOpen(): void | Promise<void> { }

	/** The picker went away — dismissed, or closed by a choice (before {@link onChooseSuggestion} runs). */
	onClose(): void | Promise<void> { }
}

/**
 * Waits a moment. A picker whose {@link SuggestModalBase.onClose} settles a pending prompt as "dismissed" waits
 * first, so a choice arriving in the same gesture — which closes the picker before it is reported — wins.
 */
export const sleep = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms))
