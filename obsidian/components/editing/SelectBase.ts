import { Component, css, html, property, query, ref, state, type HTMLTemplateResult } from "@a11d/lit"
import { popover, PopoverAlignment, PopoverPlacement } from "@3mo/popover"
import type { SelectList } from "./SelectList"
import "./SelectList"

/**
 * One row of a select's list. A plain option carries the value it stands for; an option that has to work for
 * its value — "create a new one named …" — supplies `resolve` instead and is asked only when chosen.
 *
 * The template is drawn inside the floating list, which lives outside the select's own tree, so it must style
 * itself: a self-contained element (an info chip, an item), not markup leaning on the select's stylesheet.
 */
export interface SelectOption<T> {
	readonly key: string
	readonly template: HTMLTemplateResult
	readonly value?: T
	readonly resolve?: () => T | undefined | Promise<T | undefined>
	/** Shown but not choosable — something the search found that the surface cannot take. The row says why itself. */
	readonly disabled?: boolean
}

/** How long typing settles before a search runs, so a burst of keystrokes costs one request. */
const searchDelay = 150

/**
 * Base for the searching selects: an editable-shaped field whose edit is a search.
 *
 * Idle, it shows its value (through {@link renderValue}) or its placeholder, outlined like every other
 * editable so a row of them reads as one thing. Focusing it opens a search: the face gives way to a text
 * input and a floating list of {@link search | results} follows the typing, walked with the arrow keys and
 * chosen with Enter or a click. Choosing sets the value and fires `change`, like an editable's commit.
 *
 * The list is a {@link SelectList} hosted through @3mo's `popover` directive: tethered to the field in the
 * application's top layer, positioned by the popover machinery, and no part of this element's own layout.
 *
 * Leaving without choosing — Tab, a click elsewhere, Escape — is not a commit: the typed text is dropped and
 * the field returns to whatever it showed before, its value untouched. So a half-typed search never becomes
 * a value, and a field that already had one keeps showing it as the whole item it was.
 *
 * A modified Enter (Ctrl, ⌘, Shift) is left to the surrounding form — a creation row reads those as its own
 * commit keys — and so is Escape once the list is already closed.
 *
 * Subclasses supply {@link search} and {@link renderValue}; {@link renderEmpty} may replace the placeholder
 * with a chip's own empty face.
 */
export abstract class SelectBase<T> extends Component {
	@property({ type: Object }) value?: T
	@property() placeholder = 'Select…'
	@property({ type: Boolean, reflect: true }) disabled = false

	@state() protected searching = false
	@state() protected query = ''
	@state() protected options: readonly SelectOption<T>[] = []
	@state() protected highlighted = 0
	@state() protected loading = false

	@query('.input') private readonly inputElement?: HTMLInputElement
	@query('.face') private readonly faceElement?: HTMLElement

	private searchTimer?: ReturnType<typeof setTimeout>
	private searchSequence = 0
	/** Whether the face should take focus once it renders again, so Tab carries on from this field. */
	private refocusFace = false
	/** Whether the face is being focused by the field itself rather than by the reader. */
	private quietFocus = false

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				min-width: 6ch;
				min-height: 1lh;
				outline: 1px solid transparent;
				outline-offset: .16rem;
				border-radius: 4px;
				transition: .3s ease;
				cursor: pointer;
			}

			:host(:hover) {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			@keyframes pulse {
				from { outline-color: var(--p7t-flare-accent, var(--interactive-accent)); }
				to { outline-color: var(--text-normal); }
			}

			:host([searching]) {
				animation: pulse .7s ease-in-out infinite alternate;
				cursor: text;
			}

			:host([disabled]) {
				cursor: default;
				outline-color: transparent;
			}

			[hidden] {
				display: none !important;
			}

			.field {
				display: inline-flex;
				align-items: center;
				min-width: 0;
				max-width: 100%;
				flex: 1;
			}

			.face {
				display: inline-flex;
				align-items: center;
				outline: none;
				max-width: 100%;
			}

			.placeholder {
				opacity: .4;
			}

			.input {
				all: unset;
				min-width: 6ch;
				width: 100%;
				font: inherit;
				color: inherit;
				cursor: text;
			}
		`
	}

	/** The options matching a query. An empty query should still offer something, so the list is useful before typing. */
	protected abstract search(query: string): Promise<readonly SelectOption<T>[]>

	/** The idle face of a chosen value — typically the item drawn whole. */
	protected abstract renderValue(value: T): HTMLTemplateResult

	/** The idle face with no value: the placeholder, unless a subclass has a chip with an empty state of its own. */
	protected renderEmpty(): HTMLTemplateResult {
		return html`<span class='placeholder'>${this.placeholder}</span>`
	}

	/**
	 * The list element once the directive has made it. The directive renders its popover eagerly but only at
	 * browser idle time — fine for creating the list ahead of need, far too loose for a highlight that must
	 * follow an arrow key or a list that must close on a choice. So the template below creates and tethers the
	 * list, and {@link syncList} pushes the live state into it directly on every update of the field.
	 */
	private listElement?: SelectList

	/** The floating list, as the popover directive renders it. */
	private get listTemplate(): HTMLTemplateResult {
		return html`
			<p7t-select-list
				${ref(element => this.listElement = element as SelectList | undefined)}
				mode='manual'
				.placement=${PopoverPlacement.BlockEnd}
				.alignment=${PopoverAlignment.Start}
				.offset=${6}
				?open=${this.searching}
				.rows=${this.options}
				.highlighted=${this.highlighted}
				?loading=${this.loading}
				style='min-width: ${Math.round(this.offsetWidth)}px'
				@rowhighlight=${(e: CustomEvent<number>) => this.highlightAt(e.detail)}
				@rowchoose=${(e: CustomEvent<number>) => this.chooseAt(e.detail)}>
			</p7t-select-list>
		`
	}

	protected override get template() {
		// Both faces stay in the DOM and only one shows: swapping them would remove the search input in the middle
		// of a Tab out of it, and sequential focus then loses its place instead of moving to the next cell.
		return html`
			<div class='field' ${popover(() => this.listTemplate)}>
				<input
					class='input'
					type='text'
					?hidden=${!this.searching}
					.value=${this.query}
					placeholder=${this.placeholder}
					@input=${(e: Event) => this.onInput((e.target as HTMLInputElement).value)}
					@keydown=${(e: KeyboardEvent) => this.onInputKeyDown(e)}
					@blur=${() => this.close()}>
				<div class='face' ?hidden=${this.searching} tabindex=${this.disabled ? -1 : 0} @focus=${() => this.onFaceFocus()} @click=${() => this.open()}>
					${this.value === undefined ? this.renderEmpty() : this.renderValue(this.value)}
				</div>
			</div>
		`
	}

	/** Pushes the field's live state into the list at once, rather than waiting on the directive's idle render. */
	private syncList() {
		const list = this.listElement
		if (!list) {
			return
		}

		list.rows = this.options
		list.highlighted = this.highlighted
		list.loading = this.loading
		list.style.minWidth = `${Math.round(this.offsetWidth)}px`
		list.open = this.searching
	}

	protected override updated() {
		this.toggleAttribute('searching', this.searching)
		this.syncList()
		if (this.searching) {
			this.inputElement?.focus()
		}
		else if (this.refocusFace) {
			// Focus returned by the field itself, after a choice or an Escape, must not reopen the search — only
			// focus that arrives from outside (Tab, a click) does.
			this.refocusFace = false
			this.quietFocus = true
			this.faceElement?.focus()
			this.quietFocus = false
		}
	}

	private onFaceFocus() {
		if (!this.quietFocus) {
			this.open()
		}
	}

	public override disconnectedCallback() {
		super.disconnectedCallback()
		this.close()
	}

	/** Focusing the host focuses what is live inside it: the search input while searching, else the face. */
	public override focus() {
		if (this.searching) {
			this.inputElement?.focus()
		}
		else {
			this.faceElement?.focus()
		}
	}

	/** Blurring the host ends a search the way leaving it does — dropping the typed text, keeping the value. */
	public override blur() {
		if (this.searching) {
			this.close()
		}
		else {
			this.faceElement?.blur()
		}
	}

	/** Opens the search, keeping the current value until something else is chosen. */
	public open() {
		if (this.disabled || this.searching) {
			return
		}

		this.searching = true
		this.query = ''
		this.options = []
		this.highlighted = 0
		void this.runSearch('')
	}

	/** Closes the search without choosing: the typed text is dropped and the value stands. */
	public close() {
		if (!this.searching) {
			return
		}

		clearTimeout(this.searchTimer)
		this.searchSequence++
		this.searching = false
		this.query = ''
		this.options = []
	}

	private onInput(text: string) {
		this.query = text
		clearTimeout(this.searchTimer)
		this.searchTimer = setTimeout(() => void this.runSearch(text), searchDelay)
	}

	private onInputKeyDown(e: KeyboardEvent) {
		switch (e.key) {
			case 'ArrowDown':
				e.preventDefault()
				this.highlighted = this.nextEnabled(this.highlighted, 1)
				return
			case 'ArrowUp':
				e.preventDefault()
				this.highlighted = this.nextEnabled(this.highlighted, -1)
				return
			case 'Enter':
				// A modified Enter belongs to the form around this field — a creation row's commit keys.
				if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) {
					return
				}

				e.preventDefault()
				this.chooseAt(this.highlighted)
				return
			case 'Escape':
				// The first Escape only closes the search; a second, on the idle face, reaches the form.
				e.preventDefault()
				e.stopPropagation()
				this.refocusFace = true
				this.close()
				return
		}
	}

	private async runSearch(query: string) {
		const sequence = ++this.searchSequence
		this.loading = true
		let found: readonly SelectOption<T>[] = []
		try {
			found = await this.search(query)
		}
		catch (error) {
			console.error('PLAINTORCH: a select search failed.', error)
		}

		// A slower earlier search must not land over a later one's results.
		if (sequence !== this.searchSequence || !this.searching) {
			return
		}

		this.loading = false
		this.options = found
		this.highlighted = Math.max(0, found.findIndex(option => !option.disabled))
	}

	/** The next choosable option from an index, wrapping, stepping over the disabled ones; the index itself if none. */
	private nextEnabled(from: number, direction: 1 | -1): number {
		const count = this.options.length
		for (let step = 1; step <= count; step++) {
			const index = (from + direction * step + count * step) % count
			if (!this.options[index]?.disabled) {
				return index
			}
		}

		return from
	}

	private highlightAt(index: number) {
		if (!this.options[index]?.disabled) {
			this.highlighted = index
		}
	}

	private chooseAt(index: number) {
		const option = this.options[index]
		if (option && !option.disabled) {
			void this.choose(option)
		}
	}

	private async choose(option: SelectOption<T>) {
		const value = option.resolve ? await option.resolve() : option.value
		this.refocusFace = true
		this.close()
		this.value = value
		this.dispatchEvent(new Event('change'))
	}
}
