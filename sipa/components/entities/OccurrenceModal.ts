import { Component, component, css, html, nothing, property } from "@a11d/lit"
import {
	AttentiveResolution,
	EventiveResolution,
	type Attentive,
	type AttentiveOccurrenceRef,
	type AttentiveUpdate,
	type Eventive,
	type EventiveOccurrenceRef,
	type EventiveUpdate
} from "@pleiades/sdk"
import { core, navigateToEntity, type ContextMenuEntry, type ContextMenuSpec, type IconName } from ".."
import type { EditablePart } from "../editing/EditableDataLink"
import "../editing/EditableDate"
import "../editing/EditableTime"
import { createChild, ModalBase, toast } from "../../host"

/** The occurrence a single {@link OccurrenceEditor} edits: an attentive of a decree, or an eventive of a fate/objective. */
export type OccurrenceTarget =
	| { readonly kind: 'attentive', readonly attentive: Attentive }
	| { readonly kind: 'eventive', readonly eventive: Eventive }

interface ResolutionChoice<T> {
	readonly value: T
	readonly label: string
}

const attentiveResolutions: readonly ResolutionChoice<AttentiveResolution>[] = [
	{ value: AttentiveResolution.Pending, label: 'Pending' },
	{ value: AttentiveResolution.Done, label: 'Done' },
	{ value: AttentiveResolution.Skipped, label: 'Skipped' },
]

const eventiveResolutions: readonly ResolutionChoice<EventiveResolution>[] = [
	{ value: EventiveResolution.Pending, label: 'Pending' },
	{ value: EventiveResolution.Missed, label: 'Missed' },
	{ value: EventiveResolution.Cancelled, label: 'Cancelled' },
	{ value: EventiveResolution.OptOut, label: 'Opt out' },
]

/** The owning incentive's title, used for the modal heading and the menu label. */
export function occurrenceTitle(target: OccurrenceTarget): string {
	return target.kind === 'attentive'
		? target.attentive.decree?.title ?? 'Attentive'
		: target.eventive.fate?.title ?? target.eventive.objective?.title ?? 'Eventive'
}

/** The owning incentive's id — the decree, fate, or objective the occurrence navigates to. */
function occurrenceOwnerId(target: OccurrenceTarget): string | undefined {
	return target.kind === 'attentive'
		? target.attentive.decreeId
		: target.eventive.fateId ?? target.eventive.objectiveId
}

/** The glyph and label of the owning incentive, for the "navigate" affordance. */
function occurrenceOwner(target: OccurrenceTarget): { icon: IconName, label: string } {
	if (target.kind === 'attentive') {
		return { icon: 'decree', label: 'decree' }
	}

	return target.eventive.fateId ? { icon: 'fate', label: 'fate' } : { icon: 'objective', label: 'objective' }
}

/** Addresses an attentive occurrence by its RECURRENCE-ID (attentives are always unbound, PEP111). */
function attentiveRef(attentive: Attentive): AttentiveOccurrenceRef {
	return { decreeId: attentive.decreeId, recurrenceId: attentive.recurrenceId }
}

/** Addresses an eventive occurrence by its owner id + RECURRENCE-ID. */
function eventiveRef(eventive: Eventive): EventiveOccurrenceRef {
	return { ownerId: eventive.fateId ?? eventive.objectiveId ?? '', recurrenceId: eventive.recurrenceId }
}

/**
 * Applies an update to the occurrence, folding the response back over what is known (the response carries no
 * navigation), and revalidating the surfaces that draw it. Resolves to the updated target, or `undefined` when
 * the core refused.
 */
export async function applyOccurrenceUpdate(target: OccurrenceTarget, update: AttentiveUpdate | EventiveUpdate): Promise<OccurrenceTarget | undefined> {
	let next: OccurrenceTarget | undefined
	if (target.kind === 'attentive') {
		const updated = await core.declaratives.updateAttentive(attentiveRef(target.attentive), update as AttentiveUpdate)
		next = updated && { kind: 'attentive', attentive: { ...target.attentive, ...updated, decree: updated.decree ?? target.attentive.decree } }
	}
	else {
		const updated = await core.declaratives.updateEventive(eventiveRef(target.eventive), update as EventiveUpdate)
		next = updated && {
			kind: 'eventive',
			eventive: { ...target.eventive, ...updated, fate: updated.fate ?? target.eventive.fate, objective: updated.objective ?? target.eventive.objective }
		}
	}

	if (!next) {
		toast(`PLAINTORCH could not update ${occurrenceTitle(target)}.`, 'error')
		return undefined
	}

	// The agenda and the cycle both draw occurrences; whichever is on screen re-reads itself.
	await Promise.all([
		core.repos.agenda.revalidateIfObserved(),
		core.repos.briefing.revalidateIfObserved(),
		core.repos.polaris.revalidateObserved()
	])
	return next
}

/**
 * The context menu of an occurrence (an attentive or eventive) in a list: open its editor, navigate to the
 * incentive behind it, and quick-set each of its resolutions. `changed` redraws the row that raised it.
 */
export function occurrenceMenu(target: OccurrenceTarget, changed?: (updated: OccurrenceTarget) => void): ContextMenuSpec {
	const ownerId = occurrenceOwnerId(target)
	const owner = occurrenceOwner(target)
	const current = target.kind === 'attentive' ? target.attentive.resolution : target.eventive.resolution
	const choices: readonly ResolutionChoice<number>[] = target.kind === 'attentive' ? attentiveResolutions : eventiveResolutions

	const entries: ContextMenuEntry[] = [
		{ label: 'Open', icon: 'lucide:panel-top-open', run: () => openOccurrenceModal(target, changed) },
		{ label: `Open ${owner.label}`, icon: owner.icon, disabled: !ownerId, run: () => { ownerId && void navigateToEntity(ownerId) } },
		{ separator: true },
		...choices.map((choice): ContextMenuEntry => ({
			label: choice.label,
			icon: choice.value === current ? 'lucide:check' : undefined,
			run: async () => {
				const updated = await applyOccurrenceUpdate(target, { resolution: choice.value } as AttentiveUpdate | EventiveUpdate)
				if (updated) {
					changed?.(updated)
				}
			}
		}))
	]

	return { title: occurrenceTitle(target), entries }
}

/** Opens the occurrence editor modal, relaying each committed change to `changed`. */
export function openOccurrenceModal(target: OccurrenceTarget, changed?: (updated: OccurrenceTarget) => void) {
	new OccurrenceModal(target, changed).open()
}

/**
 * A simple editor for a single occurrence — an attentive or an eventive. It sets the occurrence's resolution,
 * navigates to the decree or fate/objective behind it, and reschedules it (its date and time). It carries no
 * time allocation: allocation lives on the executive (PEP111).
 */
@component('p7t-occurrence-editor')
export class OccurrenceEditor extends Component {
	@property({ type: Object }) target?: OccurrenceTarget

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				gap: 1em;
				font-family: var(--font-interface);
				color: var(--text-normal);
				box-sizing: border-box;
				padding: 1em;
			}

			.header {
				display: flex;
				align-items: baseline;
				gap: .8ch;
				margin-block-end: .4em;
			}

			.title {
				flex: 1;
				font-size: 1.6em;
				font-weight: 250;
				line-height: 1.1;
				color: color-mix(in srgb, var(--text-normal) 88%, transparent);
			}

			.label {
				display: block;
				font-size: .8em;
				font-weight: 600;
				line-height: 1;
				color: var(--p7t-flare-accent, var(--interactive-accent));
				margin-block-end: .5em;
			}

			.field + .field {
				margin-top: .4em;
			}

			.resolutions {
				display: flex;
				flex-wrap: wrap;
				gap: .5em;
			}

			.reschedule {
				display: flex;
				align-items: center;
				gap: .6ch;
				font-weight: 300;
			}

			.open-owner {
				align-self: flex-start;
			}
		`
	}

	protected override get template() {
		const target = this.target
		if (!target) return html``

		const owner = occurrenceOwner(target)
		const ownerId = occurrenceOwnerId(target)
		const current = target.kind === 'attentive' ? target.attentive.resolution : target.eventive.resolution
		const choices: readonly ResolutionChoice<number>[] = target.kind === 'attentive' ? attentiveResolutions : eventiveResolutions
		const epoch = target.kind === 'attentive' ? target.attentive.epoch : target.eventive.epoch

		return html`
			<div class='header'>
				<div class='title'>${occurrenceTitle(target)}</div>
			</div>

			${!ownerId ? nothing : html`
				<p7t-button ghost class='open-owner' icon=${owner.icon} @click=${() => navigateToEntity(ownerId)}>Open ${owner.label}</p7t-button>
			`}

			<div class='field'>
				<span class='label'>Status</span>
				<div class='resolutions'>
					${choices.map(choice => html`
						<p7t-button
							?emphasis=${choice.value === current}
							@click=${() => this.commitResolution(choice.value)}>
							${choice.label}
						</p7t-button>
					`)}
				</div>
			</div>

			<div class='field'>
				<span class='label'>Reschedule</span>
				<div class='reschedule'>
					<p7t-editable-date
						.value=${epoch.date}
						@change=${(e: Event) => this.commitDate((e.target as EditablePart<string>).value)}>
					</p7t-editable-date>
					<p7t-editable-time
						.value=${epoch.timeOfDay}
						@change=${(e: Event) => this.commitTime((e.target as EditablePart<string>).value)}>
					</p7t-editable-time>
				</div>
			</div>
		`
	}

	private commitResolution(value: number) {
		void this.commit({ resolution: value } as AttentiveUpdate | EventiveUpdate)
	}

	private commitDate(date: string | undefined) {
		if (!date) return
		void this.commit({ date } as AttentiveUpdate | EventiveUpdate)
	}

	private commitTime(time: string | undefined) {
		// An attentive's time field is `time`; an eventive's is `startTime`. Send the one this occurrence uses.
		const update = this.target?.kind === 'attentive' ? { time: time ?? null } : { startTime: time ?? null }
		void this.commit(update as AttentiveUpdate | EventiveUpdate)
	}

	private async commit(update: AttentiveUpdate | EventiveUpdate) {
		const target = this.target
		if (!target) return

		const updated = await applyOccurrenceUpdate(target, update)
		if (!updated) return

		this.target = updated
		this.dispatchEvent(new CustomEvent<OccurrenceTarget>('occurrencechange', { detail: updated, bubbles: true, composed: true }))
	}
}

/** Hosts {@link OccurrenceEditor} in a modal, relaying every persisted change to the opener. */
export class OccurrenceModal extends ModalBase {
	constructor(
		private readonly target: OccurrenceTarget,
		private readonly onChange?: (target: OccurrenceTarget) => void,
	) {
		super()
	}

	override onOpen() {
		const editor = createChild(this.contentEl, 'p7t-occurrence-editor')
		editor.target = this.target
		editor.addEventListener('occurrencechange', e => this.onChange?.((e as CustomEvent<OccurrenceTarget>).detail))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-occurrence-editor': OccurrenceEditor
	}
}
