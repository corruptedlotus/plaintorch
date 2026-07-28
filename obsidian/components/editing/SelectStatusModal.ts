import { CSSResult } from "@a11d/lit";
import {
	DecreeStatus,
	DirectiveStatus,
	FateStatus,
	LunarDirectiveStatus,
	ObjectiveStatus
} from "@pleiades/sdk";
import { SuggestModal } from "obsidian"
import { getApp, IconName } from "..";
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise";

/** Every workflow state the status modals can prompt for (PEP100 adds the declarative + lunar kinds). */
type AnyStatus = ObjectiveStatus | DirectiveStatus | LunarDirectiveStatus | FateStatus | DecreeStatus

type StatusDescriptor<T extends AnyStatus> = {
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

/** Moonlight directives are everglow: on hold, active, or stale (PEP100). */
export const lunarDirectiveStatusDescriptors: Record<keyof typeof LunarDirectiveStatus, StatusDescriptor<LunarDirectiveStatus>> = {
	OnHold: { value: LunarDirectiveStatus.OnHold, icon: 'state-zero', label: 'On Hold' },
	Active: { value: LunarDirectiveStatus.Active, icon: 'state-active', label: 'Active' },
	Stale: { value: LunarDirectiveStatus.Stale, icon: 'state-archived', label: 'Stale' },
}

/** Fates happen rather than get done: they can be opted out of or cancelled (PEP100). */
export const fateStatusDescriptors: Record<keyof typeof FateStatus, StatusDescriptor<FateStatus>> = {
	Active: { value: FateStatus.Active, icon: 'state-active', label: 'Active' },
	OptOut: { value: FateStatus.OptOut, icon: 'state-archived', label: 'Opted Out' },
	Cancelled: { value: FateStatus.Cancelled, icon: 'state-failed', label: 'Cancelled' },
}

/** Decrees are enduring routines: active or abandoned (PEP100). */
export const decreeStatusDescriptors: Record<keyof typeof DecreeStatus, StatusDescriptor<DecreeStatus>> = {
	Active: { value: DecreeStatus.Active, icon: 'state-active', label: 'Active' },
	Abandoned: { value: DecreeStatus.Abandoned, icon: 'state-archived', label: 'Abandoned' },
}

abstract class SelectStatusModal<T extends AnyStatus> extends SuggestModal<T> {
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

export class SelectLunarDirectiveStatusModal extends SelectStatusModal<LunarDirectiveStatus> {
	static prompt = (currentValue?: LunarDirectiveStatus) => {
		const modal = new SelectLunarDirectiveStatusModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override get getDescriptors() {
		return lunarDirectiveStatusDescriptors
	}
}

export class SelectFateStatusModal extends SelectStatusModal<FateStatus> {
	static prompt = (currentValue?: FateStatus) => {
		const modal = new SelectFateStatusModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override get getDescriptors() {
		return fateStatusDescriptors
	}
}

export class SelectDecreeStatusModal extends SelectStatusModal<DecreeStatus> {
	static prompt = (currentValue?: DecreeStatus) => {
		const modal = new SelectDecreeStatusModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override get getDescriptors() {
		return decreeStatusDescriptors
	}
}
