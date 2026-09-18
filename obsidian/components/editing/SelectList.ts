import { component, css, html, nothing, property, type HTMLTemplateResult } from "@a11d/lit"
import { Popover as MoPopover } from "@3mo/popover"

/** One row of a {@link SelectList}: a key for identity and the markup to draw. */
export interface SelectListRow {
	readonly key: string
	readonly template: HTMLTemplateResult
	/** Drawn dimmed and inert: present in the results, not on offer. */
	readonly disabled?: boolean
}

/**
 * The floating list a searching select opens: @3mo's `mo-popover`, re-skinned and given rows.
 *
 * It is hosted through @3mo's `popover` directive, which tethers it to the select in the application's top
 * layer — outside the select's own tree, so it takes no part in the select's layout and no ancestor's overflow
 * clips it. Living out there is also why it is self-styled and draws its own rows: nothing in the select's
 * shadow styles reaches it, the same isolation the tooltip works under.
 *
 * It is a dumb list: the select owns the search, the highlight and the choice, and hands them in as `rows`,
 * `highlighted` and `loading`; the list reports `rowchoose` and `rowhighlight` with the row's index. A press on
 * it is kept from taking focus, so the select's input keeps the caret while a row is clicked.
 *
 * The popover machinery's click handling is switched off — it would toggle the list on a click of the anchor
 * and light-dismiss it on any other, and the select decides both.
 */
@component('p7t-select-list')
export class SelectList extends MoPopover {
	@property({ attribute: false }) rows: readonly SelectListRow[] = []
	@property({ type: Number }) highlighted = 0
	@property({ type: Boolean }) loading = false

	static override get styles() {
		return css`
			${super.styles}

			:host {
				box-sizing: border-box;
				min-width: 14rem;
				max-width: min(92vw, 28rem);
				max-height: min(60vh, 22rem);
				overflow-y: auto;
				padding: .3rem;
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 10px;
				background-color: var(--background-secondary, #1e1e1e);
				color: var(--text-normal);
				box-shadow: 0 10px 30px color-mix(in srgb, black 48%, transparent);
				font-family: var(--font-interface);
				font-size: .9rem;
				scrollbar-width: thin;
			}

			.row {
				display: flex;
				align-items: center;
				padding: .3em .5em;
				border-radius: 6px;
				cursor: pointer;
				min-width: 0;
			}

			.row > * {
				pointer-events: none;
			}

			.row.disabled {
				opacity: .45;
				cursor: default;
			}

			.row.highlighted {
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
			}

			.note {
				padding: .4em .5em;
				opacity: .55;
				font-size: .9em;
			}
		`
	}

	protected override get template() {
		return html`
			<div class='rows' @pointerdown=${(e: Event) => e.preventDefault()}>
				${this.rows.map((row, index) => html`
					<div
						class='row ${index === this.highlighted ? 'highlighted' : ''} ${row.disabled ? 'disabled' : ''}'
						@pointermove=${() => this.announce('rowhighlight', index)}
						@click=${() => this.announce('rowchoose', index)}>
						${row.template}
					</div>
				`)}
				${this.rows.length > 0 ? nothing : html`<div class='note'>${this.loading ? 'Searching…' : 'Nothing found.'}</div>`}
			</div>
		`
	}

	protected override updated(changed: Map<PropertyKey, unknown>) {
		super.updated?.(changed as never)
		if (changed.has('highlighted')) {
			this.renderRoot.querySelector('.row.highlighted')?.scrollIntoView({ block: 'nearest' })
		}
	}

	/** The select decides when the list opens and closes; the popover's own click toggling and light-dismiss stay out of it. */
	protected override handleClick() { }

	/**
	 * The `popover` directive keeps an eagerly rendered popover detached and only attaches it when it hears
	 * `openChange` — which `mo-popover` raises from the native toggle alone, and a detached element never
	 * toggles. So an `open` set from outside while detached is announced here; the directive attaches the list
	 * in response, and connecting shows it.
	 */
	protected override openUpdated() {
		if (this.open && !this.isConnected) {
			this.openChange.dispatch(true)
		}

		super.openUpdated()
	}

	public override connectedCallback() {
		super.connectedCallback()
		if (this.open) {
			this.openUpdated()
		}
	}

	private announce(type: 'rowchoose' | 'rowhighlight', index: number) {
		this.dispatchEvent(new CustomEvent<number>(type, { detail: index, bubbles: true, composed: true }))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-select-list': SelectList
	}
}
