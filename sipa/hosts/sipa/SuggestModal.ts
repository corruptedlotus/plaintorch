import { Component, component, css, html, nothing, property, query, repeat, state } from '@a11d/lit'
import { NavigabilityController } from '@3mo/navigability'
import type { SuggestLayout, SuggestShell, SuggestView } from '../../host'
import { isBackdropDismissal } from './Modal'
import { modalLayers, overlaySlot } from './modalLayers'

/**
 * A searching picker: a query input over a list — or a grid — of suggestions, in a native modal `<dialog>`.
 *
 * The cursor over the suggestions is 3MO's `NavigabilityController`. The input keeps the keyboard focus and the typing,
 * and hands the controller only the keys that move the cursor (the controller's own key listener stays unattached), so
 * the current suggestion is announced through `aria-activedescendant` on the input. In a list, Up and Down walk the
 * rows; in a grid, Up and Down move a whole row of tiles and Left and Right move one tile — at the edges of the typed
 * text, or whenever the query is empty, so the caret still moves inside a query. Enter chooses the current suggestion
 * (once the search for the typed query has answered), a click chooses the one clicked, and Escape or a click on the
 * backdrop asks to be dismissed.
 *
 * Suggestions are drawn by the view into row elements this picker provides; an answer to a query that has since changed
 * is dropped.
 */
@component('p7t-suggest-modal')
export class SuggestModal<T = unknown> extends Component {
	@property() placeholder = ''
	@property({ reflect: true }) layout: SuggestLayout = 'list'
	@state() private items: readonly T[] = []
	@state() private searching = false

	/** What a query finds, how a suggestion is drawn, and what choosing one does — set by the {@link SuggestModalShell}. */
	search: (query: string) => T[] | Promise<T[]> = () => []
	draw: (value: T, row: HTMLElement) => void = () => { }
	choose: (value: T, evt: MouseEvent | KeyboardEvent) => void = () => { }

	@query('dialog') private readonly dialog?: HTMLDialogElement
	@query('input') private readonly input?: HTMLInputElement

	private sequence = 0
	private readonly drawn = new WeakMap<Element, T>()
	/** An Enter pressed while a search was still out: it chooses from what that search finds, once it lands. */
	private pendingChoice?: KeyboardEvent

	readonly navigability = new NavigabilityController<T>(this, () => {
		const picker = this
		return {
			get items() { return picker.items },
			focus: 'activedescendant',
			// Read after rendering: null when the controller connects (so it attaches no listener of its own — the
			// picker is created, opened once and removed, never reconnected), the input once rendered (where aria goes).
			get keyboardTarget() { return picker.input ?? null },
			get orientation() { return picker.layout === 'grid' ? 'both' as const : 'vertical' as const },
			handleKeyDown: (event: KeyboardEvent) => picker.moveByRow(event),
		}
	})

	static override get styles() {
		return css`
			:host {
				display: contents;
			}

			dialog {
				box-sizing: border-box;
				width: min(700px, calc(100vw - 32px));
				max-height: calc(100vh - 20vh);
				margin-top: 10vh;
				padding: 0;
				border: 1px solid var(--background-modifier-border);
				border-radius: var(--modal-radius, 12px);
				background: var(--background-primary);
				color: var(--text-normal);
				font-family: var(--font-interface);
				box-shadow: 0 16px 48px rgba(0, 0, 0, 0.45);
				overflow: hidden;
			}

			dialog[open] {
				display: flex;
				flex-direction: column;
			}

			dialog::backdrop {
				background: rgba(0, 0, 0, 0.45);
			}

			input {
				box-sizing: border-box;
				width: 100%;
				padding: 12px 16px;
				border: none;
				border-bottom: 1px solid var(--background-modifier-border);
				background: transparent;
				color: inherit;
				font: inherit;
				font-size: 1.05em;
				outline: none;
			}

			input::placeholder {
				color: var(--text-faint);
			}

			.results {
				overflow: auto;
				padding: 6px;
			}

			:host([layout=grid]) .results {
				display: grid;
				grid-template-columns: repeat(auto-fill, minmax(7.2em, 1fr));
				gap: 4px;
			}

			.row {
				padding: 8px 12px;
				border-radius: 6px;
				cursor: pointer;
			}

			:host([layout=grid]) .row {
				display: flex;
				align-items: center;
				justify-content: center;
				padding: 1em 0.5em;
				text-align: center;
			}

			.row[data-navigability=current] {
				background: var(--background-modifier-hover);
			}

			.row small {
				display: block;
				font-size: 0.8em;
				color: var(--text-muted);
			}

			:host([layout=grid]) .row small {
				font-size: 0.7em;
				color: var(--text-faint);
			}

			.empty {
				padding: 10px 12px;
				color: var(--text-muted);
			}
		`
	}

	/** Shows the picker once it has rendered, and lists what an empty query finds. */
	async show() {
		await this.updateComplete
		if (!this.isConnected || this.dialog?.open) {
			return
		}

		this.dialog?.showModal()
		modalLayers.opened(this)
		this.input?.focus()
		void this.runSearch(this.input?.value ?? '')
	}

	/** Takes the picker down (without asking). */
	hide() {
		this.dialog?.close()
		modalLayers.closed(this)
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		modalLayers.closed(this)
	}

	/** Replaces the query, as if typed, and searches again. */
	setQuery(query: string) {
		if (this.input) {
			this.input.value = query
			this.input.focus()
		}

		void this.runSearch(query)
	}

	private async runSearch(query: string) {
		const sequence = ++this.sequence
		this.searching = true
		try {
			const found = await this.search(query)
			if (sequence !== this.sequence) {
				return
			}

			this.items = found
			await this.updateComplete
			this.navigability.goFirst()
			const pending = this.pendingChoice
			this.pendingChoice = undefined
			if (pending && this.dialog?.open) {
				this.chooseCurrent(pending)
			}
		}
		finally {
			if (sequence === this.sequence) {
				this.searching = false
				this.pendingChoice = undefined
			}
		}
	}

	private chooseCurrent(event: KeyboardEvent) {
		const current = this.navigability.current ?? this.items[0]
		if (current !== undefined) {
			this.choose(current, event)
		}
	}

	/** Grid rows: Up and Down move by the number of tiles in a row, read off the laid-out tiles. */
	private moveByRow(event: KeyboardEvent): boolean {
		if (this.layout !== 'grid' || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) {
			return false
		}

		const tiles = Array.from(this.renderRoot.querySelectorAll<HTMLElement>('.row'))
		const top = tiles[0]?.offsetTop
		const columns = Math.max(1, tiles.filter(tile => tile.offsetTop === top).length)
		const index = this.navigability.index ?? -1
		const target = Math.max(0, Math.min(this.items.length - 1, index + (event.key === 'ArrowDown' ? columns : -columns)))
		this.navigability.goTo(target, { method: 'keyboard', event })
		return true
	}

	private handleKeyDown(event: KeyboardEvent) {
		if (event.key === 'Enter') {
			event.preventDefault()
			// The suggestions on screen may still answer an earlier query; choosing among them would pick what the
			// query no longer asks for.
			if (this.searching) {
				this.pendingChoice = event
			}
			else {
				this.chooseCurrent(event)
			}
			return
		}

		const input = event.target as HTMLInputElement
		const atStart = input.selectionStart === 0 && input.selectionEnd === 0
		const atEnd = input.selectionStart === input.value.length
		const sideways = this.layout === 'grid' && (input.value === ''
			|| (event.key === 'ArrowLeft' && atStart)
			|| (event.key === 'ArrowRight' && atEnd))
		const cursorKey = ['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp'].includes(event.key)
			|| (sideways && (event.key === 'ArrowLeft' || event.key === 'ArrowRight'))
		if (cursorKey) {
			this.navigability.handleKeyDown(event)
		}
	}

	protected override updated(changed: Map<PropertyKey, unknown>) {
		super.updated(changed)
		// Rows are reused across searches; each is redrawn when the suggestion it stands for changes.
		const rows = this.renderRoot.querySelectorAll<HTMLElement>('.row')
		rows.forEach((row, index) => {
			const item = this.items[index]!
			if (this.drawn.get(row) !== item) {
				row.replaceChildren()
				this.draw(item, row)
				this.drawn.set(row, item)
			}
		})
	}

	protected override get template() {
		return html`
			<dialog part='dialog'
				@cancel=${(e: Event) => { e.preventDefault(); this.dispatchEvent(new Event('dismiss')) }}
				@click=${(e: MouseEvent) => isBackdropDismissal(this.dialog!, e) && this.dispatchEvent(new Event('dismiss'))}>
				<input type='text' placeholder=${this.placeholder} autocomplete='off' spellcheck='false'
					@input=${(e: InputEvent) => void this.runSearch((e.target as HTMLInputElement).value)}
					@keydown=${(e: KeyboardEvent) => this.handleKeyDown(e)}>
				<div class='results' role='listbox'>
					${this.items.length === 0 && !this.searching ? html`<div class='empty'>No results.</div>` : nothing}
					${repeat(this.items, (_, index) => index, (item, index) => html`
						<div class='row' role='option' ${this.navigability.item({ index, data: item })}
							@pointermove=${() => this.navigability.index !== index && this.navigability.goTo(index, { method: 'pointer' })}
							@click=${(e: MouseEvent) => this.choose(item, e)}></div>
					`)}
				</div>
				<slot name=${overlaySlot}></slot>
			</dialog>
		`
	}
}

/**
 * Hosts a {@link SuggestView} (a `SuggestModalBase` picker) in a {@link SuggestModal}, created on open and removed on
 * close. A choice goes to the view, which decides whether the picker closes.
 */
export class SuggestModalShell<T> implements SuggestShell {
	private element?: SuggestModal<T>

	constructor(private readonly view: SuggestView<T>) { }

	open(): void {
		if (this.element) {
			return
		}

		const element = new SuggestModal<T>()
		element.placeholder = this.view.placeholder
		element.layout = this.view.layout
		element.search = query => this.view.getSuggestions(query)
		element.draw = (value, row) => this.view.renderSuggestion(value, row)
		element.choose = (value, evt) => this.view.selectSuggestion(value, evt)
		element.addEventListener('dismiss', () => this.close())
		document.body.append(element)
		this.element = element
		this.view.handleOpen()
		void element.show()
	}

	close(): void {
		const element = this.element
		if (!element) {
			return
		}

		this.element = undefined
		element.hide()
		element.remove()
		this.view.handleClose()
	}

	setQuery(query: string): void {
		this.element?.setQuery(query)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-suggest-modal': SuggestModal
	}
}
