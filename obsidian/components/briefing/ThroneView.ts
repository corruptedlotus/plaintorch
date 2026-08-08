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

			:host > * {
				flex: 1 0 24em;
			}

			/* The agenda stacks above the Polaris cycle in the right-hand column: the agenda takes its natural
			   height and the cycle fills the rest, scrolling its own body. */
			.polaris-column {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				min-height: 0;
			}

			.polaris-column > p7t-briefing-agenda {
				flex: 0 0 auto;
			}

			.polaris-column > p7t-briefing-polaris {
				flex: 1 1 auto;
				min-height: 0;
			}
		`
	}

	override get template() {
		return html`
			<p7t-briefing-onrush .data=${this.data?.currentOnrush}></p7t-briefing-onrush>
			<div class='polaris-column'>
				<p7t-briefing-agenda></p7t-briefing-agenda>
				<p7t-briefing-polaris .data=${this.data?.currentPolaris}></p7t-briefing-polaris>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-throne-view': ThroneView
	}
}
