import { Component, component, css, html, nothing, property, query, state } from '@a11d/lit'
import { IconName } from 'components/PleiadesIcon'

/** How prominently the trigger floats: not at all, as the main FAB, or as a smaller secondary one beside it. */
export type ExpandingActionsSize = false | 'main' | 'secondary'

/** One choice offered when an expanding action opens. */
export interface ExpandingAction {
	readonly key: string
	readonly icon: IconName
	readonly label: string
	readonly run: () => void | Promise<void>
}

/**
 * A single button that opens into a set of choices.
 *
 * The panel is a popover, so it renders in the top layer: the same component works inside a scrolling
 * grid cell, where an absolutely positioned panel would be clipped by the scroll container, and light
 * dismissal comes for free.
 *
 * It lays the choices out in a row and falls back to a column only when the row will not fit beside the
 * trigger, which is measured against the real viewport rather than assumed from a breakpoint.
 */
@component('p7t-expanding-actions')
export class ExpandingActions extends Component {
	@property({ type: Array }) actions: readonly ExpandingAction[] = []
	@property() icon: IconName = 'lucide:plus'
	/** Optional text on the trigger. Without it the trigger is icon-only. */
	@property() label?: string
	/**
	 * The floating-button presentation: `'main'` is the full-size accent FAB, `'secondary'` a smaller one to sit
	 * beside it. Reflected as the `large` attribute so `[large]` selectors match either; the bare attribute
	 * (`large` with no value, the long-standing form) reads as `'main'`.
	 */
	@property({
		reflect: true,
		converter: {
			fromAttribute: (value: string | null): ExpandingActionsSize => value === null ? false : value === 'secondary' ? 'secondary' : 'main',
			toAttribute: (value: ExpandingActionsSize) => value === false ? null : value
		}
	}) large: ExpandingActionsSize = false
	@property() actionLabel = 'Add'

	@state() private stacked = false

	@query('.panel') private readonly panelElement!: HTMLElement
	@query('.trigger') private readonly triggerElement!: HTMLElement

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
			}

			/*.trigger {
				display: flex;
				align-items: center;
				justify-content: center;
				gap: .4em;
				border-radius: 8px;
				padding: .25em;
				min-width: 1.9em;
				min-height: 1.9em;
				box-sizing: border-box;
				cursor: pointer;
				color: inherit;
				background: transparent;
				border: none;
				font-family: var(--font-interface);
				font-size: 1em;
				transition: background-color .2s ease;
			}

			.trigger:hover {
				background-color: color-mix(in srgb, var(--text-normal) 14%, transparent);
			}

			.trigger p7t-icon {
				width: 1.25em;
				height: 1.25em;
			}*/

			:host([large]) .trigger {
				&::part(button) {
					justify-content: center;
					min-width: 3em;
					min-height: 3em;
					border-radius: 999px;
					background-color: var(--interactive-accent);
					color: var(--text-on-accent);
					box-shadow: 0 4px 14px rgb(0 0 0 / .35);
				}
				
				&:hover::part(button) {
					background-color: color-mix(in srgb, var(--interactive-accent) 85%, white);
				}
			}

			/* The secondary FAB: the main one's face at a smaller size, to sit beside it without competing. */
			:host([large='secondary']) .trigger::part(button) {
				min-width: 2.3em;
				min-height: 2.3em;
				padding: .3em;
				box-shadow: 0 3px 10px rgb(0 0 0 / .3);
			}


			.panel {
				position: fixed;
				margin: 0;
				padding: .3em;
				border: 1px solid var(--background-modifier-border, color-mix(in srgb, var(--text-normal) 20%, transparent));
				border-radius: 10px;
				background-color: var(--background-secondary, #2b2b2b);
				box-shadow: 0 6px 20px rgb(0 0 0 / .4);
				display: flex;
				flex-direction: row;
				gap: .25em;
				overflow: visible;
				font-family: var(--font-interface);
			}

			.panel:not(:popover-open) {
				display: none;
			}

			.panel[data-stacked] {
				flex-direction: column;
			}

			.choice {
				font-family: inherit;
				font-size: .95em;
				font-weight: 300;
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-button
				ghost
				?large=${this.large === 'main'}
				class='trigger'
				aria-label=${this.actionLabel}
				label=${this.actionLabel}
				.icon=${this.icon}
				@click=${(e: MouseEvent) => this.onTriggerClick(e)}>
				${!this.label ? nothing : html`<span>${this.label}</span>`}
			</p7t-button>
			<div class='panel' popover='auto' ?data-stacked=${this.stacked}>
				${this.actions.map(action => html`
					<p7t-button ghost class='choice' icon=${action.icon} @click=${() => this.choose(action)}>
						${action.label}
					</p7t-button>
				`)}
			</div>
		`
	}

	private onTriggerClick(e: MouseEvent) {
		// The grid row underneath treats a click as navigation, and the trigger is not that.
		console.log('Trigger clicked', e)
		if (this.actions.length === 0) {
			return
		}
		
		this.panelElement.showPopover()
		this.place()
		e.stopPropagation()
	}

	private async choose(action: ExpandingAction) {
		this.panelElement.hidePopover()
		await action.run()
	}

	/**
	 * Places the panel beside the trigger, preferring a row and stacking only when one will not fit.
	 *
	 * Measured after showing rather than predicted, because the width of a row depends on the labels it
	 * was given, which the component does not know until it has them.
	 */
	private place() {
		const anchor = this.triggerElement.getBoundingClientRect()
		const panel = this.panelElement
		panel.removeAttribute('data-stacked')
		this.stacked = false

		const margin = 8
		const rowWidth = panel.offsetWidth
		// Opens leftward: these live in the rightmost column and at the bottom-right of the view.
		const fitsAsRow = anchor.right - rowWidth >= margin

		if (!fitsAsRow) {
			this.stacked = true
			panel.setAttribute('data-stacked', '')
		}

		const width = panel.offsetWidth
		const height = panel.offsetHeight
		const left = Math.max(margin, Math.min(anchor.right - width, window.innerWidth - width - margin))
		const below = anchor.bottom + margin
		const top = below + height > window.innerHeight - margin
			? Math.max(margin, anchor.top - height - margin)
			: below

		panel.style.left = `${left}px`
		panel.style.top = `${top}px`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-expanding-actions': ExpandingActions
	}
}
