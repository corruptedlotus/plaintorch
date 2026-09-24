import { component, css, html, state } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Objective, ObjectiveStatus, OnrushSprint } from "@pleiades/sdk"
import { core, objectiveStatusDescriptors, SelectStatusModal } from '..'
import { toast } from '../../host'

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
		const objective = this.objective
		if (!objective) return

		let status: ObjectiveStatus | undefined
		try {
			status = await SelectStatusModal.prompt(objectiveStatusDescriptors)
		}
		catch {
			return
		}

		if (status === undefined) return

		// The response is absorbed into the canonical instance on the way back, so every surface showing this
		// objective updates without the picker telling any of them.
		const shifted = await core.repos.objectives.mutate(objective.id, async () =>
			await core.objectives.shiftWorkflow(objective.id, { status }))
		if (shifted) {
			toast(`${shifted.title}: ${ObjectiveStatus[status]}`, 'success')
		} else {
			toast('Failed to update objective status.', 'error')
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
