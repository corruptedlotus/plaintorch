import { component, css, html, state } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Objective, ObjectiveStatus, OnrushSprint } from "@pleiades/sdk"
import { isInactiveStatus, shiftState, type Status } from '..'

@component('p7t-objective-item')
export class ObjectiveItem extends EntityItem<Objective> {

	@state() currentOnrush?: OnrushSprint

	protected get objective() {
		return this.entity
	}

	static override get styles() {
		return css`
			${super.styles}

			.college {
				width: 36px;
				align-self: stretch;
				display: flex;
				align-items: center;
				justify-content: center;
			}
		`
	}

	/** An objective done with — done, archived or failed — is drawn dimmed. */
	override get disabled() {
		return this.entity !== undefined && isInactiveStatus(ObjectiveStatus[this.entity.status] as Status)
	}

	/**
	 * The notch shifts the objective's state through the state bubble, which saves it and shows what became of it. The
	 * answer is absorbed into the canonical instance, so every surface showing the objective follows.
	 */
	protected override async notchAction(notch: HTMLElement) {
		if (this.objective) {
			await shiftState(notch, this.objective)
		}
	}

	protected override get preTitle() {
		// The chip carries the directive glyph, the title, the "World Quest" placeholder, and the mini-banner tooltip.
		return html`<p7t-directive-item small .directive=${this.objective!.directive}></p7t-directive-item>`
	}

	protected override get info() {
		return html`<p7t-celestron-item ?starfire=${this.currentOnrush && (this.currentOnrush.id === this.objective?.onrushSprintId)} small .value=${this.objective!.celestronValue}></p7t-celestron-item>`
	}

	protected override get notchTemplate() {
		return html`
			<p7t-status-item icon-only .status=${ObjectiveStatus[this.objective!.status] as keyof typeof ObjectiveStatus}></p7t-status-item>
		`
	}

	protected override get highlightInfo() {
		return html`
			<div class='college'>
				<p7t-college-item mode='icon' .college=${this.objective!.college}></p7t-college-item>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item': ObjectiveItem
	}
}
