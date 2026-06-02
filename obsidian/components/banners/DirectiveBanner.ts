import { component, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Directive } from '@pleiades/sdk'
import { core } from ".."

@component('p7t-directive-banner')
export class DirectiveBanner extends EntityBanner<Directive> {
	protected override get info() {
		return html`
			<span></span>
		`
	}

	override fetchEntity(puck: string): Promise<Directive | undefined> {
		return core.directives.get(puck)
	}

	protected override get headingTemplate() {
		return html`<span>${this.entity?.title}</span>`
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