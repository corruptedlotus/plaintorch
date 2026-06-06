import { Component } from "@a11d/lit";
import { Objective, ObjectiveStatus } from "@pleiades/sdk";
import { App, Notice, SuggestModal } from "obsidian"
import { core } from "..";

export class ChangeStateModal extends SuggestModal<keyof typeof ObjectiveStatus> {
	constructor(app: App, protected readonly host: Component & { entity?: Objective }) {
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