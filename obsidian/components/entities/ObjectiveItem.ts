import { component, css, html } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Objective, ObjectiveCollege, ObjectiveStatus } from "@pleiades/sdk"
import { App, Notice, SuggestModal } from "obsidian"
import { core, statusDescriptors } from 'components'

@component('p7t-objective-item')
export class ObjectiveItem extends EntityItem<Objective> {

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
		if (!this.entity) return
		new ChangeStateModal((window as any).app! as App, this).open()
	}

	protected override get preTitle() {
		return !this.entity!.directive ? html`
			<div style='display: flex; align-items: center; gap: 4px; opacity: .4; font-weight: 400; font-size: .9em; line-height: .9'>
				<span>World Quest</span>
			</div>
		` : html`
			<div style='display: flex; align-items: center; gap: 4px; opacity: .6; font-weight: 400; font-size: .9em; line-height: .9'>
				<p7t-icon style='width: 20px; height: 20px;' icon='directive'></p7t-icon>
				<span>${this.entity!.directive!.title}</span>
			</div>
		`
	}

	protected override get info() {
		return html`
			<div style='display: flex; align-items: center; gap: 2px; font-weight: 300; line-height: .9'>
				<span class='celestron'>${this.entity!.celestronValue}</span>
				<p7t-icon style='width: 20px; height: 20px;' icon='starfire'></p7t-icon>
			</div>
		`
	}

	protected override get extraAction() {
		return html`
			<p7t-icon class='notch-icon' icon='lucide:chevron-right'></p7t-icon>
		`
	}

	protected override get notch() {
		return html`
			<p7t-icon
				icon=${statusDescriptors[ObjectiveStatus[this.entity!.status] as keyof typeof ObjectiveStatus]?.icon}>
			</p7t-icon>
		`
	}

	protected override get highlightInfo() {
		let college = ObjectiveCollege[this.entity!.college]
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

class ChangeStateModal extends SuggestModal<keyof typeof ObjectiveStatus> {
	constructor(app: App, protected readonly host: ObjectiveItem) {
		super(app);
	}

	override getSuggestions(query: string) {
		return Object.keys(typeof ObjectiveStatus).map(x => ObjectiveStatus[Number(x)]) as (keyof typeof ObjectiveStatus)[]
	}

	renderSuggestion(state: keyof typeof ObjectiveStatus, el: HTMLElement) {
		const item = el.createEl('p7t-status-item')
		item.status = state
	}

	override async onChooseSuggestion(item: keyof typeof ObjectiveStatus, _: MouseEvent | KeyboardEvent) {
		const results = await core.objectives.shiftWorkflow(this.host.entity!.id, { status: ObjectiveStatus[item] })
		if (!!results) {
			this.host.entity = results
			new Notice(`${this.host.entity!.title}: ${item}`)
		} else {
			new Notice(`Failed to update objective status.`)
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item': ObjectiveItem
	}
}