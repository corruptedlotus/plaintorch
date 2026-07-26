import { component, css, html, HTMLTemplateResult } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Objective, ObjectiveCollege, ObjectiveStatus } from "@pleiades/sdk"
import { App } from "obsidian"
import { ChangeStateModal, core, statusDescriptors } from 'components'

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
				height: auto;
				align-self: stretch;
				display: flex;
				flex-direction: column;
				justify-content: stretch;
				align-items: stretch;

				& span {
					font-size: .4em;
					font-weight: 600;
					text-transform: uppercase;
					opacity: .6;
					line-height: .8;
					text-align: center;
				}

				& p7t-icon {
					width: 100%;
					height: auto;
					flex: 1 0 auto;
				}
			}
		`
	}

	protected override async notchAction() {
		if (!this.objective) return
		new ChangeStateModal((window as any).app! as App, this).open()
	}

	protected override get preTitle() {
		return !this.objective!.directive ? html`
			<div style='display: flex; align-items: center; gap: 4px; opacity: .4; font-weight: 400; font-size: .9em; line-height: .9'>
				<span>World Quest</span>
			</div>
		` : html`
			<div style='display: flex; align-items: center; gap: 4px; opacity: .6; font-weight: 400; font-size: .9em; line-height: .9'>
				<p7t-icon style='width: 20px; height: 20px;' icon='directive'></p7t-icon>
				<span>${this.objective!.directive!.title}</span>
			</div>
		`
	}

	protected override get info() {
		return html`
			<div style='display: flex; align-items: center; gap: 2px; font-weight: 300; line-height: .9'>
				<span class='celestron'>${this.objective!.celestronValue}</span>
				<p7t-icon style='width: 20px; height: 20px;' icon='starfire'></p7t-icon>
			</div>
		`
	}

	protected override get extraActionTemplate(): HTMLTemplateResult | undefined {
		return html`
			<p7t-icon class='notch-icon' icon='lucide:chevron-right'></p7t-icon>
		`
	}

	protected override async extraAction() {
		if (await core.polaris.addObjectiveToCurrent(this.objective!.id)) {
			const ev = new CustomEvent<void>('updateRequest', { bubbles: true, composed: true })
			this.dispatchEvent(ev)
		}
	}

	protected override get notchTemplate() {
		return html`
			<p7t-icon
				icon=${statusDescriptors[ObjectiveStatus[this.objective!.status] as keyof typeof ObjectiveStatus]?.icon}>
			</p7t-icon>
		`
	}

	protected override get highlightInfo() {
		let college = ObjectiveCollege[this.objective!.college]
		college = college === 'Unspecified' ? 'None' : college

		return html`
			<div class='college'>
				<p7t-icon
					icon='college-${college.toLowerCase()}'>
				</p7t-icon>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item': ObjectiveItem
	}
}