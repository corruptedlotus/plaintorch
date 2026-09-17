import { Component, css, eventListener, html, state, type HTMLTemplateResult } from "@a11d/lit"

/** What a committed creation row does next: open the new entity's editor, or start another row. */
export type CreationCommitMode = 'open' | 'again'

/** Detail of the `created` event a row fires once its entity exists. */
export interface CreationRowCreated<T> {
	readonly entity: T
	readonly mode: CreationCommitMode
}

/**
 * Base for the inline creation rows — a temporary row that stands where the new entity will appear and holds
 * the cells that make it, in place of a modal.
 *
 * The row floats a little above its list, wider than an ordinary item, so it reads as a form being filled
 * rather than an item that already exists. Its cells are the ordinary editables and selects, so Tab walks them
 * as it walks any form. The keys that end it are the row's own, whatever cell has focus:
 *
 * - **Ctrl+Enter** (or ⌘+Enter) creates the entity and opens its editor at once;
 * - **Shift+Enter** creates the entity and starts a fresh row for the next one;
 * - **Escape** cancels the row.
 *
 * A plain Enter stays with the cell it was pressed in — it finishes that cell's edit, as everywhere else.
 * Before the entity is made, whichever cell still has focus is blurred so its pending edit lands first.
 *
 * A subclass supplies its {@link cells}, how to {@link create} the entity from them, how to
 * {@link openEditor} on it, and how to {@link reset} for the next one. The row fires `created` with the entity
 * and the mode, and `cancel`; the host decides whether the row stays (a `created` in `again` mode) or goes.
 */
export abstract class CreationRowBase<T> extends Component {
	@state() protected busy = false

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .35em;
				position: relative;
				z-index: 3;
				margin-inline: -.6em;
				margin-block: .3em;
				padding: .7em .9em .5em;
				border-radius: 12px;
				background-color: var(--background-secondary, #1e1e1e);
				border: 1px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 45%, transparent);
				box-shadow: 0 8px 26px color-mix(in srgb, black 45%, transparent);
				font-family: var(--font-interface);
				animation: settle .2s ease;
			}

			@keyframes settle {
				from {
					opacity: 0;
					transform: translateY(-.4em);
				}
			}

			:host([busy]) {
				opacity: .7;
				pointer-events: none;
			}

			.cells {
				display: flex;
				align-items: center;
				gap: 1em;
				flex-wrap: wrap;
			}

			.cell {
				display: inline-flex;
				flex-direction: column;
				gap: .2em;
			}

			.cell .caption {
				font-size: .7em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .04em;
				opacity: .55;
			}

			.cell.grow {
				flex: 1 1 12em;
			}

			.hints {
				display: flex;
				gap: 1.2em;
				font-size: .72em;
				opacity: .5;
			}

			kbd {
				font-family: inherit;
				font-weight: 600;
			}
		`
	}

	/** The row's cells, each usually wrapped as `<div class='cell'><span class='caption'>…</span>…</div>`. */
	protected abstract get cells(): HTMLTemplateResult

	/**
	 * Makes the entity from the cells' values. Resolves to nothing when the row is not ready — a missing required
	 * choice, a refused write — having told the reader why; the row then stays open.
	 */
	protected abstract create(): Promise<T | undefined>

	/** Opens the editor the new entity is assigned to. */
	protected abstract openEditor(entity: T): void

	/** Clears the cells for the next entity. */
	protected abstract reset(): void

	protected override get template() {
		return html`
			<div class='cells'>${this.cells}</div>
			<div class='hints'>
				<span><kbd>Tab</kbd> next cell</span>
				<span><kbd>Ctrl+Enter</kbd> create & edit</span>
				<span><kbd>Shift+Enter</kbd> create & next</span>
				<span><kbd>Esc</kbd> cancel</span>
			</div>
		`
	}

	protected override firstUpdated() {
		this.focusFirstCell()
	}

	protected override updated() {
		this.toggleAttribute('busy', this.busy)
	}

	@eventListener({ type: 'keydown', target: this })
	protected onKeyDown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			e.preventDefault()
			e.stopPropagation()
			this.cancel()
			return
		}

		if (e.key !== 'Enter') {
			return
		}

		if (e.ctrlKey || e.metaKey) {
			e.preventDefault()
			e.stopPropagation()
			void this.commit('open')
		}
		else if (e.shiftKey) {
			e.preventDefault()
			e.stopPropagation()
			void this.commit('again')
		}
	}

	/** Cancels the row: nothing is made, and the host is told to take it away. */
	public cancel() {
		this.dispatchEvent(new CustomEvent<void>('cancel', { bubbles: true, composed: true }))
	}

	/** Creates the entity from the cells, then does what the mode says. */
	public async commit(mode: CreationCommitMode) {
		if (this.busy) {
			return
		}

		// A cell still being edited commits on blur; let that land before the values are read.
		this.blurActiveCell()
		await this.updateComplete

		this.busy = true
		let entity: T | undefined
		try {
			entity = await this.create()
		}
		finally {
			this.busy = false
		}

		if (entity === undefined) {
			this.focusFirstCell()
			return
		}

		this.dispatchEvent(new CustomEvent<CreationRowCreated<T>>('created', { detail: { entity, mode }, bubbles: true, composed: true }))
		if (mode === 'open') {
			this.openEditor(entity)
		}
		else {
			this.reset()
			await this.updateComplete
			this.focusFirstCell()
		}
	}

	/** Focuses the first focusable cell, so the row is ready to type into the moment it appears. */
	protected focusFirstCell() {
		const first = this.shadowRoot?.querySelector<HTMLElement>('.cells [tabindex], .cells [contenteditable], .cells input, .cells p7t-editable-time-unit, .cells p7t-activity-select, .cells p7t-timeframe-select')
		first?.focus()
	}

	private blurActiveCell() {
		const active = this.shadowRoot?.activeElement as HTMLElement | null
		active?.blur()
	}
}
