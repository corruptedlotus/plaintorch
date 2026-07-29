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
		const objectiveId = this.host.entity!.id
		// The response is absorbed into the canonical instance on the way back, so every surface showing
		// this objective updates without the modal telling any of them.
		const results = await core.repos.objectives.mutate(objectiveId, async () =>
			await core.objectives.shiftWorkflow(objectiveId, { status: ObjectiveStatus[item] }))
		if (!!results) {
			new Notice(`${results.title}: ${item}`)
		} else {
			new Notice(`Failed to update objective status.`)
		}
	}
}