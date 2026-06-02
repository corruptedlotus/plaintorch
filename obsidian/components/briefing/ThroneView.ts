import { Component, component, css, html, property } from '@a11d/lit'
import { SystemBriefing } from '@pleiades/sdk'

@component('p7t-throne-view')
export class ThroneView extends Component {
	@property({ type: Object }) data?: SystemBriefing
	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: stretch;
				gap: 1em;
				flex-wrap: wrap;
			}

			* {
				flex: 1 0 24em;
			}
		`
	}

	override get template() {
		return html`
			<p7t-briefing-onrush .data=${this.data?.currentOnrush}></p7t-briefing-onrush>
			<p7t-briefing-polaris .data=${this.data?.currentPolaris}></p7t-briefing-polaris>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-throne-view': ThroneView
	}
}