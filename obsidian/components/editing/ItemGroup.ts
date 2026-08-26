import { Component, component, css, html, nothing, property } from "@a11d/lit"

/**
 * A generic editable list of things. It draws each item of an array through a caller-supplied renderer, gives each
 * a tiny remove affordance, and offers a subtle add button as the trailing cell. It is **controlled** — it owns no
 * state and never mutates the array; the host passes the items and the add/remove handlers, so any kind of item (a
 * college, a tag, an endpoint) can be grouped without this component knowing what it is.
 *
 * @example
 * html`<p7t-item-group
 *   .items=${colleges}
 *   .renderItem=${(c) => html`<p7t-college-item .college=${c as ObjectiveCollege}></p7t-college-item>`}
 *   .onAdd=${() => this.pickCollege()}
 *   .onRemove=${(c) => this.dropCollege(c as ObjectiveCollege)}>
 * </p7t-item-group>`
 */
@component('p7t-item-group')
export class ItemGroup extends Component {
	/** The items to draw. The host owns the array; this only reads it. */
	@property({ attribute: false }) items: readonly unknown[] = []

	/** Renders one item's face — the content shown beside its remove button. */
	@property({ attribute: false }) renderItem: (item: unknown, index: number) => unknown = () => nothing

	/** Adds an item; invoked by the trailing add button. Omit to hide the add affordance. */
	@property({ attribute: false }) onAdd?: () => void

	/** Removes one item; invoked by its remove button. Omit to hide the per-item remove. */
	@property({ attribute: false }) onRemove?: (item: unknown, index: number) => void

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				flex-wrap: wrap;
				align-items: center;
				gap: .4em;
			}

			.item {
				display: inline-flex;
				align-items: center;
				gap: .1ch;
			}

			/* The remove and add affordances read as small, quiet marks beside their items. */
			.item p7t-button,
			.add {
				font-size: .7em;
			}

			.none {
				opacity: .4;
			}
		`
	}

	protected override get template() {
		const empty = this.items.length === 0
		return html`
			${this.items.map((item, index) => html`
				<span class='item'>
					${this.renderItem(item, index)}
					${!this.onRemove ? nothing : html`
						<p7t-button ghost danger icon='lucide:x' label='Remove' @click=${() => this.onRemove!(item, index)}></p7t-button>
					`}
				</span>
			`)}
			${empty && !this.onAdd ? html`<span class='none'>None</span>` : nothing}
			${!this.onAdd ? nothing : html`
				<p7t-button class='add' ghost icon='lucide:plus' label='Add' @click=${() => this.onAdd!()}></p7t-button>
			`}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-item-group': ItemGroup
	}
}
