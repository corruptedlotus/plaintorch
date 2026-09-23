import { Component, component, css, html, property, state } from '@a11d/lit'
import { SystemBriefing } from '@pleiades/sdk'

@component('p7t-throne-view')
export class ThroneView extends Component {
	@property({ type: Object }) data?: SystemBriefing

	/**
	 * The agenda and the Polaris cycle form a mutually-exclusive collapsible pair: exactly one is expanded.
	 * The state lives here so a toggle on either card flips both. Default: the cycle expanded, the agenda
	 * collapsed to its attentive count.
	 */
	@state() private orbitsExpanded = false
	@state() private agendaExpanded = false

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

			.polaris-column {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				min-height: 0;
			}

			/* The expanded card of the pair fills the column and scrolls its own body; the collapsed one
			   shrinks to its header. Driven by the reflected 'collapsed' attribute on each card. */
			.polaris-column > :not([collapsed]) {
				flex: 1 1 auto;
				min-height: 0;
			}

			.polaris-column > [collapsed] {
				flex: 0 0 auto;
			}
		`
	}

	private togglePolarisColumn() {
		this.orbitsExpanded = !this.orbitsExpanded
	}

	private toggleOnrushColumn() {
		this.agendaExpanded = !this.agendaExpanded
	}

	override get template() {
		return html`
			<div class='polaris-column'>
				<p7t-briefing-agenda
					collapsible
					mode='eventives'
					?collapsed=${!this.agendaExpanded}
					@collapsetoggle=${() => this.toggleOnrushColumn()}>
				</p7t-briefing-agenda>
				<p7t-briefing-onrush
					collapsible
					?collapsed=${this.agendaExpanded}
					@collapsetoggle=${() => this.toggleOnrushColumn()}
					.data=${this.data?.currentOnrush}>
				</p7t-briefing-onrush>
			</div>
			<div class='polaris-column'>
				<p7t-briefing-agenda
					collapsible
					mode='attentives'
					?collapsed=${!this.orbitsExpanded}
					@collapsetoggle=${() => this.togglePolarisColumn()}>
				</p7t-briefing-agenda>
				<p7t-briefing-polaris
					collapsible
					?collapsed=${this.orbitsExpanded}
					.data=${this.data?.currentPolaris}
					@collapsetoggle=${() => this.togglePolarisColumn()}>
				</p7t-briefing-polaris>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-throne-view': ThroneView
	}
}
