import { Component, component, css, eventListener, html, state } from "@a11d/lit"
import { SystemBriefing } from "@pleiades/sdk"
import { core } from ".."

@component('p7t-briefing')
export class Briefing extends Component {
	@state() page = 'throne'
	@state() data?: SystemBriefing

	@eventListener('keyNavigationRequest')
	protected onKeyNavigationRequest(e: CustomEvent<string>) {
		e.stopPropagation()
	}

	@eventListener('updateRequest')
	protected onUpdateRequest(e: CustomEvent<void>) {
		core.system.getBriefing().then(briefing => this.data = briefing)
		e.stopPropagation()
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
				<p7t-navitem key='throne' icon='everglow' active>Throne Room</p7t-navitem>
				<p7t-navitem key='directives' icon='directive'>Backlog</p7t-navitem>
				<p7t-navitem key='moonlight' icon='objective-lunar'>Moonlight</p7t-navitem>
				<p7t-navitem key='forecast' icon='polaris'>Forecast</p7t-navitem>
				<p7t-navitem key='planning' icon='onrush'>Planning</p7t-navitem>
			</div>
			${this.content}
		`
	}

	protected get content() {
		switch (this.page) {
			case 'throne':
				return html`<p7t-throne-view .data=${this.data}></p7t-throne-view>`
			default:
				return html`<div class='empty'>Nothing to show</div>`
		}
	}

	protected override async initialized() {
		const briefing = await core.system.getBriefing()
		this.data = briefing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing': Briefing
	}
}