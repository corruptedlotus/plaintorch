import { Component, component, css, event, html, nothing, property } from '@a11d/lit'
import type { IconName } from '../PleiadesIcon'

/**
 * A committed search filter, drawn as a chip split in two, like a split button: its body names the filter and its
 * value (`Status: Active`), and its far side removes it. Clicking the body asks for the chip to be taken apart and
 * edited, which the search's controller does by writing it back into the field as its clause.
 */
@component('p7t-filter-chip')
export class FilterChip extends Component {
	/** The filter's name: "Status". */
	@property() heading = ''
	/** The value's name: "Active". */
	@property() label = ''
	/** The value's glyph, or else the filter's. */
	@property() icon?: IconName

	/** The body was clicked: the chip is to be taken apart and edited. */
	@event() chipedit!: EventDispatcher
	/** The far side was clicked: the chip is to go. */
	@event() chipremove!: EventDispatcher

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: stretch;
				max-width: 100%;
				/* The chip radius: the briefing hero's info chip, and an item's notch and parts. */
				border-radius: 8px;
				border: 1px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 40%, transparent);
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 14%, transparent);
				color: var(--text-normal);
				font-family: var(--font-interface);
				font-size: .9em;
				line-height: 1.2;
				overflow: hidden;
			}

			button {
				all: unset;
				display: inline-flex;
				align-items: center;
				cursor: pointer;
				transition: background-color .2s ease, color .2s ease;
			}

			button:hover,
			button:focus-visible {
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 22%, transparent);
			}

			button:focus-visible {
				outline: 1px solid var(--p7t-flare-accent, var(--interactive-accent));
				outline-offset: -2px;
			}

			.body {
				gap: .35em;
				min-width: 0;
				padding: .2em .55em .2em .6em;
			}

			.body p7t-icon {
				flex: none;
				width: 1.1em;
				height: 1.1em;
			}

			.heading {
				color: var(--text-muted);
			}

			.label {
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
			}

			.remove {
				padding-inline: .35em .45em;
				border-inline-start: 1px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 30%, transparent);
				color: var(--text-muted);
			}

			.remove:hover,
			.remove:focus-visible {
				color: var(--text-normal);
			}

			.remove p7t-icon {
				width: .95em;
				height: .95em;
			}
		`
	}

	protected override get template() {
		return html`
			<button class='body' type='button' aria-label=${`Edit filter ${this.heading}: ${this.label}`} @click=${() => this.chipedit.dispatch()}>
				${!this.icon ? nothing : html`<p7t-icon icon=${this.icon}></p7t-icon>`}
				<span class='heading'>${this.heading}:</span>
				<span class='label'>${this.label}</span>
			</button>
			<button class='remove' type='button' aria-label=${`Remove filter ${this.heading}: ${this.label}`} @click=${() => this.chipremove.dispatch()}>
				<p7t-icon icon='lucide:x'></p7t-icon>
			</button>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-filter-chip': FilterChip
	}
}
