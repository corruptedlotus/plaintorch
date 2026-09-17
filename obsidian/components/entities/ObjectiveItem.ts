import { component, css, html, state } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Objective, ObjectiveStatus, OnrushSprint } from "@pleiades/sdk"
import { App } from "obsidian"
import { ChangeStateModal } from 'components'

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

	override get disabled() {
		return (this.entity?.status ?? 99) > ObjectiveStatus.Done
	}

	protected override async notchAction() {
		if (!this.objective) return
		new ChangeStateModal((window as any).app! as App, this).open()
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

	protected override get compactDirective() {
		return this.objective?.directive
	}

	protected override get compactKind() {
		return 'Objective'
	}

	protected override get compactChips() {
		return html`
			<p7t-college-item small mode='named' .college=${this.objective!.college}></p7t-college-item>
			<p7t-celestron-item small .value=${this.objective!.celestronValue}></p7t-celestron-item>
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
