import { component, css, html, HTMLTemplateResult } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Objective, ObjectiveStatus } from "@pleiades/sdk"
import { App } from "obsidian"
import { ChangeStateModal, core } from 'components'

@component('p7t-objective-item')
export class ObjectiveItem extends EntityItem<Objective> {

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
		return html`<p7t-celestron-item small .value=${this.objective!.celestronValue}></p7t-celestron-item>`
	}

	protected override get extraActionTemplate(): HTMLTemplateResult | undefined {
		return html`
			<p7t-icon class='notch-icon' icon='lucide:chevron-right'></p7t-icon>
		`
	}

	protected override async extraAction() {
		const objectiveId = this.objective!.id
		const added = await core.repos.objectives.mutate(objectiveId, async () =>
			await core.polaris.addObjectiveToCurrent(objectiveId))
		if (added) {
			// The objective and briefing ride on the mutate above; only the owning cycle needs a nudge.
			await core.repos.polaris.revalidateObserved()
		}
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
