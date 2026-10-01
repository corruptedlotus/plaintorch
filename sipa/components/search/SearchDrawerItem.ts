import { Component, component, css, html, nothing, property } from '@a11d/lit'
import type { IconName } from '../PleiadesIcon'

/**
 * One row of a search box's drawer: a filter to start writing (`Status`, with its `status:` key as a hint), or one of a
 * filter's values (`Active`, perhaps with a note such as where a directive sits). The drawer is a floating
 * `p7t-select-list` outside the box's own tree, so the row styles itself.
 */
@component('p7t-search-drawer-item')
export class SearchDrawerItem extends Component {
	@property() icon?: IconName
	@property() label = ''
	/** A quieter line beside the label: what a filter does, where a value sits. */
	@property() detail?: string
	/** A trailing hint in the clause's own notation, such as a filter's `status:`. */
	@property() hint?: string

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
				gap: .55em;
				width: 100%;
				min-width: 0;
				font-family: var(--font-interface);
			}

			p7t-icon {
				flex: none;
				width: 1.15em;
				height: 1.15em;
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, var(--text-normal));
			}

			.text {
				display: flex;
				align-items: baseline;
				gap: .6em;
				min-width: 0;
				flex: 1;
			}

			.label {
				flex: none;
			}

			.detail {
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
				color: var(--text-muted);
				font-size: .85em;
			}

			.hint {
				flex: none;
				color: var(--text-faint, var(--text-muted));
				font-family: var(--font-monospace);
				font-size: .82em;
			}
		`
	}

	protected override get template() {
		return html`
			${!this.icon ? nothing : html`<p7t-icon icon=${this.icon}></p7t-icon>`}
			<span class='text'>
				<span class='label'>${this.label}</span>
				${!this.detail ? nothing : html`<span class='detail'>${this.detail}</span>`}
			</span>
			${!this.hint ? nothing : html`<span class='hint'>${this.hint}</span>`}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-search-drawer-item': SearchDrawerItem
	}
}
