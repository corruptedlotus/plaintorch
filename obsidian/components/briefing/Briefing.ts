import { Component, component, css, eventListener, html, state } from "@a11d/lit"
import { core, DerivedRef } from ".."

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
				
				display: flex;
				padding-inline: .5em;
				gap: .3em;
			}
		`
	}

	override get template() {
		return html`
			<p7t-briefing-hero .briefing=${this.data} style='margin-bottom: .1em'></p7t-briefing-hero>
			<div class='navbar'>
				<p7t-navitem key='throne' icon='everglow' ?active=${this.page === 'throne'}>Throne Room</p7t-navitem>
				<p7t-navitem key='forecast' icon='polaris' ?active=${this.page === 'forecast'}>Forecast</p7t-navitem>
				<p7t-navitem key='planning' icon='onrush' ?active=${this.page === 'planning'}>Planning</p7t-navitem>
				<p7t-navitem key='directives' icon='directive' ?active=${this.page === 'directives'}>Directives</p7t-navitem>
				<p7t-navitem key='objectives' icon='objective' ?active=${this.page === 'objectives'}>Objectives</p7t-navitem>
			</div>
			${this.content}
		`
	}

	protected get content() {
		switch (this.page) {
			case 'throne':
				return html`<p7t-throne-view .data=${this.data}></p7t-throne-view>`
			case 'directives':
				return html`<p7t-entity-grid></p7t-entity-grid>`
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