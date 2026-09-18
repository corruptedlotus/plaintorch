import { Component, css, eventListener, html, query, state, type HTMLTemplateResult } from "@a11d/lit"

/** What a committed creation row does next: open the new entity's editor, or start another row. */
export type CreationCommitMode = 'open' | 'again'

/** Detail of the `created` event a row fires once its entity exists. */
export interface CreationRowCreated<T> {
	readonly entity: T
	readonly mode: CreationCommitMode
}

/** Numbers each row's anchor, so two rows on one page never position against each other. */
let anchorSequence = 0

/**
 * Base for the inline creation rows — a temporary row that stands where the new entity will appear and holds
 * the cells that make it, in place of a modal.
 *
 * The host stays in the list's flow and reserves the row's height, but the row itself is drawn in the top
 * layer, anchored to the host: a card that scrolls or clips its content cannot cut the row off, and the row
 * still follows the host as the list scrolls. CSS anchor positioning does the following where it exists; a
 * measured fallback keeps up otherwise.
 *
 * Its cells are the ordinary editables and selects, so Tab walks them as it walks any form. The keys that end
 * it are the row's own, wherever focus is:
 *
 * - **Ctrl+Enter** (or ⌘+Enter) creates the entity and opens its editor at once;
 * - **Shift+Enter** creates the entity and starts a fresh row for the next one;
 * - **Escape** cancels the row — from inside it, or from anywhere else on the page;
 * - a pointer landing outside the row cancels it too.
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

	@query('.panel') private readonly panelElement!: HTMLElement
	@query('.placeholder') private readonly placeholderElement!: HTMLElement

	private readonly anchorName = `--p7t-creation-row-${++anchorSequence}`
	private sizeObserver?: ResizeObserver

	static override get styles() {
		return css`
			:host {
				display: block;
				position: relative;
				margin-block: .3em;
			}

			/* Reserves the row's height in the list while the row itself floats in the top layer. */
			.placeholder {
				min-height: 4em;
			}

			.panel {
				position: fixed;
				inset: auto;
				margin: 0;
				box-sizing: border-box;
				display: flex;
				flex-direction: column;
				gap: .35em;
				padding: .7em .9em .5em;
				border-radius: 12px;
				background-color: var(--background-secondary, #1e1e1e);
				color: var(--text-normal);
				border: 1px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 45%, transparent);
				box-shadow: 0 8px 26px color-mix(in srgb, black 45%, transparent);
				font-family: var(--font-interface);
				animation: settle .2s ease;
				overflow: visible;
			}

			/* Anchored where supported: the panel sits on the host, a little wider than it, and follows it as it scrolls. */
			@supports (anchor-name: --x) {
				.panel {
					top: anchor(top);
					left: calc(anchor(left) - .6em);
					width: calc(anchor-size(width) + 1.2em);
				}
			}

			.panel:not(:popover-open) {
				display: none;
			}

			@keyframes settle {
				from {
					opacity: 0;
					transform: translateY(-.4em);
				}
			}

			:host([busy]) .panel {
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
			<!-- The in-flow stand-in the panel anchors to; both live in this shadow tree, as anchor names do not cross one. -->
			<div class='placeholder' style='anchor-name: ${this.anchorName}'></div>
			<div class='panel' popover='manual' style='position-anchor: ${this.anchorName}'>
				<div class='cells'>${this.cells}</div>
				<div class='hints'>
					<span><kbd>Tab</kbd> next cell</span>
					<span><kbd>Ctrl+Enter</kbd> create & edit</span>
					<span><kbd>Shift+Enter</kbd> create & next</span>
					<span><kbd>Esc</kbd> cancel</span>
				</div>
			</div>
		`
	}

	protected override connected() {
		document.addEventListener('pointerdown', this.onDocumentPointerDown, true)
		document.addEventListener('keydown', this.onDocumentKeyDown)
		window.addEventListener('scroll', this.reposition, true)
		window.addEventListener('resize', this.reposition)
	}

	protected override disconnected() {
		document.removeEventListener('pointerdown', this.onDocumentPointerDown, true)
		document.removeEventListener('keydown', this.onDocumentKeyDown)
		window.removeEventListener('scroll', this.reposition, true)
		window.removeEventListener('resize', this.reposition)
		this.sizeObserver?.disconnect()
		this.sizeObserver = undefined
		try {
			this.panelElement?.hidePopover()
		}
		catch {
			// Already hidden.
		}
	}

	protected override firstUpdated() {
		try {
			this.panelElement.showPopover()
		}
		catch {
			// Unsupported or already shown; the panel still renders, in flow.
		}

		// The host reserves what the floating panel takes, so the list around it lays out as if the row were in it.
		this.sizeObserver = new ResizeObserver(() => {
			this.placeholderElement.style.minHeight = `${this.panelElement.offsetHeight}px`
			this.reposition()
		})
		this.sizeObserver.observe(this.panelElement)
		this.reposition()
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

	/**
	 * Escape from anywhere on the page cancels the row. One pressed inside it is handled by the row's own listener
	 * first (and by an open select before that, which only closes its list) and never reaches here.
	 */
	private readonly onDocumentKeyDown = (e: KeyboardEvent) => {
		if (e.key === 'Escape' && !e.defaultPrevented && !this.busy) {
			this.cancel()
		}
	}

	/** A pointer landing anywhere but the row (its floating panel and the lists its selects open included) cancels it. */
	private readonly onDocumentPointerDown = (e: PointerEvent) => {
		if (this.busy || e.composedPath().includes(this)) {
			return
		}

		this.cancel()
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

	/** The measured fallback for engines without anchor positioning: the panel is placed over the host by hand. */
	private readonly reposition = () => {
		const panel = this.panelElement
		if (!panel || CSS.supports('anchor-name: --x')) {
			return
		}

		const host = this.placeholderElement.getBoundingClientRect()
		panel.style.top = `${Math.round(host.top)}px`
		panel.style.left = `${Math.round(host.left - 10)}px`
		panel.style.width = `${Math.round(host.width + 20)}px`
	}
}
