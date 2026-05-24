import { component, html } from "@a11d/lit"
import { EntityBanner } from '../EntityBanner'
import { Directive } from '@pleiades/sdk'

@component('p7t-directive-banner')
export class DirectiveBanner extends EntityBanner<Directive> {
	protected override get info() {
		return html`
			<span>Status</span>
		`
	}

	protected override get heading() {
		return html`Pleiades Directive`
	}

	protected override get actions() {
		return html``
	}

	override icon = 'directive'
}

declare global {
	interface HTMLTagNameMap {
		'p7t-directive-banner': DirectiveBanner
	}
}