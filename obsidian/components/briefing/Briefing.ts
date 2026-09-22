import { Component, component, css, eventListener, html, state } from "@a11d/lit"
import { core, DerivedRef, getApp, PreferenceModal } from ".."

@component('p7t-briefing')
export class Briefing extends Component {
	@state() page = 'throne'

	private readonly briefingRef = new DerivedRef(this, core.repos.briefing)

	get data() {
		return this.briefingRef.value
	}

	@eventListener('requestKeyNavigation')
	protected onRequestKeyNavigation(e: CustomEvent<string>) {
		e.stopPropagation()
		if (e.detail) {
			this.page = e.detail
		}
	}

	/** Opens the settings modal. The settings sit at the end of the tab row but act as a button, not a nav tab. */
	private openSettings() {
		new PreferenceModal(getApp()).open()
	}

	static override get styles() {
		return css`
			:host {
				height: 100%;
				display: grid;
				align-items: stretch;
				grid-template-rows: auto auto 1fr;
				gap: .5em;
			}

			.navbar {
				position: relative;
				display: flex;
				justify-content: center;
				padding-inline: .5em;
				margin-block: -1em .5em;
				align-items: center;
				gap: .3em;
				border-bottom: 1px solid color-mix(in srgb, var(--interactive-accent) 30%, transparent);

				/*&::before, &::after {
					content: '';
					flex: 1;
					height: 1px;
					margin-inline: 1em;
					background-color: color-mix(in srgb, var(--interactive-accent) 30%, transparent);
				}*/
			}

			.settings {
				position: absolute;
				right: .4em;
				top: 50%;
				transform: translateY(-50%);
				display: inline-flex;
				align-items: center;
				justify-content: center;
				padding: 6px;
				border: none;
				border-radius: 8px;
				background: transparent;
				color: var(--text-muted);
				cursor: pointer;
				transition: .2s ease;
			}

			.settings:hover {
				background-color: var(--background-secondary);
				color: var(--text-normal);
			}

			.settings p7t-icon {
				width: 20px;
				height: 20px;
			}
		`
	}

	override get template() {
		return html`
			<p7t-briefing-hero .briefing=${this.data} style='margin-bottom: .1em'></p7t-briefing-hero>
			<div class='navbar'>
				<p7t-navitem key='throne' icon='everglow' ?active=${this.page === 'throne'}>Throne Room</p7t-navitem>
				<p7t-navitem key='planning' icon='onrush' ?active=${this.page === 'planning'}>Planning</p7t-navitem>
				<p7t-navitem key='backlog' icon='directive' ?active=${this.page === 'backlog'}>Backlog</p7t-navitem>
				<p7t-navitem key='lore' icon='lorepage' ?active=${this.page === 'lore'}>Lore</p7t-navitem>
				<button class='settings' aria-label='Settings' @click=${() => this.openSettings()}>
					<p7t-icon icon='lucide:settings'></p7t-icon>
				</button>
			</div>
			${this.content}
		`
	}

	protected get content() {
		switch (this.page) {
			case 'throne':
				return html`<p7t-throne-view .data=${this.data}></p7t-throne-view>`
			case 'backlog':
				return html`<p7t-entity-grid></p7t-entity-grid>`
			case 'lore':
				return html`<p7t-lore-grid></p7t-lore-grid>`
			case 'planning':
				return html`<p7t-dependency-canvas></p7t-dependency-canvas>`
			default:
				return html`<div class='empty'>Nothing to show</div>`
		}
	}

}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing': Briefing
	}
}