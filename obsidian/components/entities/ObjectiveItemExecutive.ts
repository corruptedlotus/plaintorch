import { component } from "@a11d/lit"
import { ObjectiveItem } from "./ObjectiveItem"

@component('p7t-objective-item-exec')
export class ObjectiveItemExecutive extends ObjectiveItem {

	protected override get extraActionTemplate() {
		return undefined
	}

	protected override get notchTemplate() {
		// if done, inherit super, otherwise show a <p7t-time-unit>
		
	} 
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item-exec': ObjectiveItemExecutive
	}
}