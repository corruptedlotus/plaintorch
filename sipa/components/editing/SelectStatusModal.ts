import { CSSResult } from "@a11d/lit";
import {
	DecreeStatus,
	DirectiveStatus,
	FateStatus,
	LunarDirectiveStatus,
	ObjectiveStatus
} from "@pleiades/sdk";
import { IconName } from "..";
import { createChild, sleep, SuggestModalBase } from "../../host"
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise";

/** Every workflow state the status modals can prompt for (PEP100 adds the declarative + lunar kinds). */
export type AnyStatus = ObjectiveStatus | DirectiveStatus | LunarDirectiveStatus | FateStatus | DecreeStatus

export type StatusDescriptor<T extends AnyStatus> = {
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

/**
 * Picks a workflow status from one kind's status table — the objective, directive, lunar directive, fate or decree
 * descriptors above. One picker for every kind: the table is all that differs between them.
 */
export class SelectStatusModal<T extends AnyStatus> extends SuggestModalBase<T> {
	protected dpe?: DeferredPromiseExecutor<T | undefined>

	constructor(private readonly descriptors: Record<string, StatusDescriptor<T>>) {
		super()
	}

	/**
	 * Opens the picker over a status table. Resolves with the chosen status and rejects when dismissed, which a
	 * `p7t-editable`'s `doEdit` reads as a cancel.
	 */
	static prompt<T extends AnyStatus>(descriptors: Record<string, StatusDescriptor<T>>): Promise<T | undefined> {
		const modal = new SelectStatusModal(descriptors)
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override getSuggestions() {
		return Object.values(this.descriptors).map(x => x.value)
	}

	renderSuggestion(state: T, el: HTMLElement) {
		const item = createChild(el, 'p7t-icon-item')
		item.data = state
		const descriptor = Object.values(this.descriptors).find(x => x.value === state)!
		item.icon = descriptor.icon
		item.text = descriptor.label
		item.style.color = descriptor.colour?.toString() || 'currentColor'
	}

	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}

	override async onChooseSuggestion(item: T) {
		this.dpe?.resolve(item)
	}
}
