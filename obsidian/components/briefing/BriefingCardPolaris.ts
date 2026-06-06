import { component, css, html } from "@a11d/lit"
import { BriefingCard } from "./BriefingCard"
import { PolarisCycle } from "@pleiades/sdk"
import { core } from ".."

@component('p7t-briefing-polaris')
export class BriefingCardPolaris extends BriefingCard<PolarisCycle> {
	override readonly icon = 'polaris'
	override readonly preHeading = 'Active Polaris Cycle'

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: #038899;
			}
		`
	}

	protected override get offlineTemplate() {
		return html`
			<span class='no-data'>Polaris Rests in the Void</span>
			<p7t-button @click=${() => this.begin()} class='start-button' icon='polaris'>Begin Cycle</p7t-button>
		`
	}

	override get headingTemplate() {
		return html`<p7t-elapsed-view .epoch=${this.data!.startTime}></p7t-elapsed-view>`
	}

	protected override get listContent() {
		return html`
			${this.data!.executives.map(executive => html`
				<p7t-objective-item-exec interactive .entity=${executive.objective}></p7t-objective-item-exec>
			`)}
		`
	}

	protected override get footer() {
		return html`
			<div></div>
			<p7t-button @click=${() => this.conclude()}>Conclude</p7t-button>
		`
	}

	private begin() {
		if (this.data) return
		core.polaris.startNew().then(polaris => this.data = polaris)
	}

	private conclude() {
		if (!this.data) return
		core.polaris.end(new Date().toISOString()).then(polaris => {
			if (!!polaris) this.data = undefined
		})
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-polaris': BriefingCardPolaris
	}
}