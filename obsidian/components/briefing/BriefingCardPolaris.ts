import { component, css, html } from "@a11d/lit"
import { BriefingCard } from "./BriefingCard"
import { PolarisCycle } from "@pleiades/sdk"

@component('p7t-briefing-polaris')
export class BriefingCardPolaris extends BriefingCard<PolarisCycle> {
	override readonly preHeading = 'Active Polaris Cycle'

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: #189d81;
			}

			.add-button {
				margin: .4em 1.2em;
			}
		`
	}

	override get headingTemplate() {
		return html`<span>04:22</span>`
	}

	protected override get listContent() {
		return html`
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-entity-item .entity=${{ title: 'Sample Objective', id: 'LOL' }}></p7t-entity-item>
			<p7t-button class='add-button'>Add Objective</p7t-button>
		`
	}

	protected override get offlineTemplate() {
		return html`
			<span class='no-data'>The Polaris rises anew...</span>
			<p7t-button class='start-button' icon='polaris'>Begin Cycle</p7t-button>
		`
	}

	protected override get footer() {
		return html`
			<p7t-value-progress icon='starfire' value=30 max=47></p7t-value-progress>
			<p7t-button>Conclude</p7t-button>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-polaris': BriefingCardPolaris
	}
}