import { component, css, html, property } from "@a11d/lit"
import { ObjectiveItem } from "./ObjectiveItem"
import { Executive } from "@pleiades/sdk"
import { ExecutiveModal, getApp } from ".."

@component('p7t-objective-item-exec')
export class ObjectiveItemExecutive extends ObjectiveItem {

	static override get styles() {
		return css`
			${super.styles}

			p7t-time-unit {
				font-size: 1.7em;
				font-weight: 400;
				margin: .1em;
			}
		`
	}

	@property({
		updated(this: ObjectiveItemExecutive, value: Executive | undefined) {
			this.entity = value?.objective
		}
	}) executive?: Executive

	protected override get extraActionTemplate() {
		return undefined
	}

	protected override async notchAction() {
		if (!this.executive) return
		new ExecutiveModal(getApp(), this.executive, executive => {
			this.executive = executive
			this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
		}).open()
	}

	protected override get notchTemplate() {
		// if done, inherit super, otherwise show the time still left against the estimation
		return this.executive?.executed ? super.notchTemplate : html`
			<p7t-time-unit .value=${Math.max(0, (this.executive?.estimation ?? 0) - (this.executive?.elapsed ?? 0))}></p7t-time-unit>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item-exec': ObjectiveItemExecutive
	}
}
