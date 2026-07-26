import { component, html, property } from "@a11d/lit"
import { ObjectiveItem } from "./ObjectiveItem"
import { Executive } from "@pleiades/sdk"

@component('p7t-objective-item-exec')
export class ObjectiveItemExecutive extends ObjectiveItem {

	@property({
		updated(this: ObjectiveItemExecutive, value: Executive | undefined) {
			this.entity = value?.objective
		}
	}) executive?: Executive

	protected override get extraActionTemplate() {
		return undefined
	}

	protected override get notchTemplate() {
		// if done, inherit super, otherwise show a <p7t-time-unit>
		return this.executive?.executed ? super.notchTemplate : html`
			<p7t-time-unit .value=${this.executive?.estimation - this.executive?.}></p7t-time-unit>
		`
	} 
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item-exec': ObjectiveItemExecutive
	}
}