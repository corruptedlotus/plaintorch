import { component, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Objective } from '@pleiades/sdk'
import { ObjectiveStatus } from "@pleiades/sdk/plaintorch/node"
import { OnrushSprint } from "@pleiades/sdk"
import { App, Notice, SuggestModal } from "obsidian"
import { core } from ".."

@component('p7t-objective-banner')
export class ObjectiveBanner extends EntityBanner<Objective> {
	override icon = 'objective'

	override async fetchEntity(puck: string) {
		return core.objectives.get(puck)
	}

	pickOnrush = () => {
		new AddToAvailableOnrushModal(this.app!, this.entity!).open()
	}

	protected override get info() {
		return html`
			<span>${this.entity?.statusName}</span>
			<span>Directive: ${this.entity?.directive?.title ?? '-'}</span>
			<span>Starfire: ${this.entity?.celestronValue}</span>
		`
	}

	protected override get headingTemplate() {
		return html`<span>${this.entity?.title ?? '???'}</span>`
	}
	
	protected override get preHeadingTemplate() {
		return html`<span>Pleiades Objective</span>`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-status-item
				.status=${ObjectiveStatus[this.entity!.status] as keyof typeof ObjectiveStatus}>
			</p7t-status-item>
		`
	}

	protected override get actions() {
		return html`
			<p7t-button icon='onrush' @click=${() => this.pickOnrush()}>Add to Onrush</p7t-button>
			<p7t-button icon='polaris'>Add to Polaris</p7t-button>
		`
	}
}

class AddToAvailableOnrushModal extends SuggestModal<OnrushSprint> {
	constructor(app: App, protected readonly objective: Objective) {
		super(app);
	}

	override async getSuggestions(query: string) {
		return await core.onrush.available()
	}

	renderSuggestion(sprint: OnrushSprint, el: HTMLElement) {
		el.createEl('div', { text: sprint.title });
		el.createEl('small', { text: sprint.id });
	}

	override async onChooseSuggestion(item: OnrushSprint, evt: MouseEvent | KeyboardEvent) {
		if (await core.objectives.addToOnrush(this.objective!.id, item.id)) {
			new Notice(`Added to ${(item.id === '0' ? 'planning' : 'active')} Onrush.`)
		}
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-objective-banner': ObjectiveBanner
	}
}