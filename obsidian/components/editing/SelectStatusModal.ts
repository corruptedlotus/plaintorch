import { CSSResult } from "@a11d/lit";
import { Directive, DirectiveStatus, Objective, ObjectiveStatus } from "@pleiades/sdk";
import { Notice, SuggestModal } from "obsidian"
import { core, getApp, IconName } from "..";
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise";

type StatusDescriptor<T extends ObjectiveStatus | DirectiveStatus> = {
	value: T,
	icon: IconName,
	label: string,
	colour?: CSSResult,
}

export const objectiveStatusDescriptors: Record<keyof typeof ObjectiveStatus, StatusDescriptor<ObjectiveStatus>> = {
	Standby: { value: ObjectiveStatus.Standby, icon: 'state-zero', label: 'Standby' },
	Blocked: { value: ObjectiveStatus.Blocked, icon: 'state-blocked', label: 'Blocked' },
	Onrush: { value: ObjectiveStatus.Onrush, icon: 'state-onrush', label: 'Onrush' },
	Polaris: { value: ObjectiveStatus.Polaris, icon: 'state-polaris', label: 'Polaris' },
	Done: { value: ObjectiveStatus.Done, icon: 'state-done', label: 'Done' },
	Archived: { value: ObjectiveStatus.Archived, icon: 'state-archived', label: 'Archived' },
	Failed: { value: ObjectiveStatus.Failed, icon: 'state-failed', label: 'Failed' },
}

export const directiveStatusDescriptors: Record<keyof typeof DirectiveStatus, StatusDescriptor<DirectiveStatus>> = {
	Planned: { value: DirectiveStatus.Planned, icon: 'state-zero', label: 'Planned' },
	Committed: { value: DirectiveStatus.Committed, icon: 'state-commit', label: 'Committed' },
	Active: { value: DirectiveStatus.Active, icon: 'state-active', label: 'Active' },
	Fulfilled: { value: DirectiveStatus.Fulfilled, icon: 'state-done', label: 'Fulfilled' },
	Over: { value: DirectiveStatus.Over, icon: 'state-archived', label: 'Over' },
	Failed: { value: DirectiveStatus.Failed, icon: 'state-failed', label: 'Failed' },
}

abstract class SelectStatusModal<T extends ObjectiveStatus | DirectiveStatus> extends SuggestModal<T> {
	protected dpe?: DeferredPromiseExecutor<T | undefined>
	abstract get getDescriptors(): Record<string, StatusDescriptor<T>>

	override getSuggestions(query: string) {
		return Object.values(this.getDescriptors).map(x => x.value as T)
	}

	renderSuggestion(state: T, el: HTMLElement) {
		const item = el.createEl('p7t-icon-item')
		item.data = state
		const descriptor = Object.values(this.getDescriptors).find(x => x.value === state)!
		item.icon = descriptor.icon
		item.text = descriptor.label
		item.style.color = descriptor.colour?.toString() || 'currentColor'
	}

	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}

	override async onChooseSuggestion(item: T, _: MouseEvent | KeyboardEvent) {
		this.dpe?.resolve(item)
	}
}

export class SelectObjectiveStatusModal extends SelectStatusModal<ObjectiveStatus> {
	static prompt = (currentValue?: ObjectiveStatus) => {
		const modal = new SelectObjectiveStatusModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override get getDescriptors() {
		return objectiveStatusDescriptors
	}
}

export class SelectDirectiveStatusModal extends SelectStatusModal<DirectiveStatus> {
	static prompt = (currentValue?: DirectiveStatus) => {
		const modal = new SelectDirectiveStatusModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override get getDescriptors() {
		return directiveStatusDescriptors
	}
}