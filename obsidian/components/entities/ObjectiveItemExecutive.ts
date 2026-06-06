import { component } from "@a11d/lit"
import { ObjectiveItem } from "./ObjectiveItem"

@component('p7t-objective-item-exec')
export class ObjectiveItemExecutive extends ObjectiveItem {

	protected override get extraActionTemplate() {
		return undefined
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item-exec': ObjectiveItemExecutive
	}
}