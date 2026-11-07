import { Component, component, css, html, nothing, property } from "@a11d/lit"
import { DependencyEndpointKind, TerminativeResolution, type Terminative, type TerminativeOccurrenceRef, type TerminativeOriginKind, type TerminativeUpdate } from "@pleiades/sdk"
import { core, navigateToEntity, type ContextMenuEntry, type ContextMenuSpec, type IconName } from ".."
import { EntityDetailModal } from "../canvas/EntityDetailModal"
import { createChild, ModalBase, toast } from "../../host"
import { terminativeStateDescriptors } from "./occurrenceDescriptors"
import { occurrenceTime } from "./occurrenceTime"
import "../system/DatetimeView"

/** The glyph and label each kind of origin is named with, for the "navigate" affordance. */
const originDescriptors: Record<TerminativeOriginKind, { icon: IconName, label: string }> = {
	objective: { icon: 'objective', label: 'objective' },
	directive: { icon: 'directive', label: 'directive' },
	checkpoint: { icon: 'checkpoint', label: 'checkpoint' },
}

/** The id of the item whose due a terminative is. Read from the ids, so a copy made field by field reads right too. */
export function terminativeOriginId(terminative: Terminative): string {
	return terminative.objectiveId ?? terminative.directiveId ?? terminative.checkpointId ?? ''
}

/** Which kind of item a terminative is the due of. */
export function terminativeOriginKind(terminative: Terminative): TerminativeOriginKind {
	return terminative.objectiveId ? 'objective' : terminative.directiveId ? 'directive' : 'checkpoint'
}

/** The origin's glyph and label. */
export function terminativeOrigin(terminative: Terminative): { icon: IconName, label: string } {
	return originDescriptors[terminativeOriginKind(terminative)]
}

/** The origin's title, used for the row, the modal heading and the menu label. */
export function terminativeTitle(terminative: Terminative): string {
	return (terminative.objective ?? terminative.directive ?? terminative.checkpoint)?.title ?? 'Due'
}

/**
 * Navigates to the item whose due a terminative is: an objective's or a directive's note, as an occurrence opens its
 * owner's, and a checkpoint — which has no note — through its banner in a modal.
 */
export async function openTerminativeOrigin(terminative: Terminative) {
	const originId = terminativeOriginId(terminative)
	if (!originId) return
	if (terminativeOriginKind(terminative) === 'checkpoint') {
		new EntityDetailModal(DependencyEndpointKind.Checkpoint, { id: originId, title: terminativeTitle(terminative) }).open()
		return
	}

	await navigateToEntity(originId)
}

/**
 * Addresses a terminative by its origin, so a still-projected one hardens on interaction; a hardened one by its row id.
 */
function terminativeRef(terminative: Terminative): TerminativeOccurrenceRef {
	return terminative.id ? { id: terminative.id } : { originId: terminativeOriginId(terminative) }
}

/**
 * Applies the one change a user makes to a terminative — its ignore flag — and revalidates the surfaces that draw it.
 * The response carries the origin. Resolves to the updated terminative, or `undefined` when the core refused.
 */
export async function applyTerminativeUpdate(terminative: Terminative, update: TerminativeUpdate): Promise<Terminative | undefined> {
	const updated = await core.declaratives.updateTerminative(terminativeRef(terminative), update)
	if (!updated) {
		toast(`PLAINTORCH could not update the due of ${terminativeTitle(terminative)}.`, 'error')
		return undefined
	}

	await Promise.all([
		core.repos.agenda.revalidateIfObserved(),
		core.repos.briefing.revalidateIfObserved(),
		core.repos.polaris.revalidateObserved()
	])
	return updated
}

/**
 * The context menu of a terminative in a list: open its editor, navigate to its origin, and raise or lower its ignore
 * flag — the one thing about it a user sets. `changed` redraws the row that raised it.
 */
export function terminativeMenu(terminative: Terminative, changed?: (updated: Terminative) => void): ContextMenuSpec {
	const origin = terminativeOrigin(terminative)
	const entries: ContextMenuEntry[] = [
		{ label: 'Open', icon: 'lucide:panel-top-open', run: () => openTerminativeModal(terminative, changed) },
		{ label: `Open ${origin.label}`, icon: origin.icon, disabled: !terminativeOriginId(terminative), run: () => void openTerminativeOrigin(terminative) },
		{ separator: true },
		{
			label: terminative.ignored ? 'Stop ignoring' : 'Ignore',
			icon: terminative.ignored ? 'lucide:eye' : 'lucide:eye-off',
			run: async () => {
				const updated = await applyTerminativeUpdate(terminative, { ignored: !terminative.ignored })
				if (updated) {
					changed?.(updated)
				}
			}
		}
	]

	return { title: terminativeTitle(terminative), entries }
}

/** Opens the terminative editor modal, relaying each committed change to `changed`. */
export function openTerminativeModal(terminative: Terminative, changed?: (updated: Terminative) => void) {
	new TerminativeModal(terminative, changed).open()
}

/**
 * A single terminative (PEP103 patch 7): the due of an objective, a stellar directive or a checkpoint. Its place in time
 * and its resolution follow its origin, so the editor only reads them; it navigates to the origin and raises or lowers
 * the ignore flag, which takes the terminative off the agenda.
 */
@component('p7t-terminative-editor')
export class TerminativeEditor extends Component {
	@property({ type: Object }) terminative?: Terminative

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

			.reading {
				display: flex;
				align-items: center;
				gap: .6ch;
				font-weight: 300;

				& p7t-icon {
					width: 1.2em;
					height: 1.2em;
				}
			}

			.hint {
				font-size: .8em;
				opacity: .6;
			}

			.actions {
				display: flex;
				flex-wrap: wrap;
				gap: .5em;
			}
		`
	}

	protected override get template() {
		const terminative = this.terminative
		if (!terminative) return html``

		const origin = terminativeOrigin(terminative)
		const state = terminativeStateDescriptors[terminative.resolution]
		const time = occurrenceTime(terminative.epoch)
		const pending = terminative.resolution === TerminativeResolution.Pending

		return html`
			<div class='header'>
				<div class='title'>${terminativeTitle(terminative)}</div>
			</div>

			<div class='field'>
				<span class='label'>Due</span>
				<div class='reading'>
					<p7t-icon icon='terminative'></p7t-icon>
					<p7t-datetime-view relative warn=${pending ? 'past' : 'none'}
						.date=${time.date}
						.time=${time.time}
						.anchor=${time.anchor?.label}
						.granularity=${terminative.epoch.granularity}>
					</p7t-datetime-view>
				</div>
			</div>

			<div class='field'>
				<span class='label'>Status</span>
				<div class='reading'>
					<p7t-icon icon=${state.icon}></p7t-icon>${state.label}${!terminative.ignored ? nothing : html` · Ignored`}
				</div>
				<div class='hint'>${terminativeOriginKind(terminative) === 'checkpoint'
					? 'Follows the checkpoint: pending until it unlocks, its due passed or not.'
					: `Follows the ${origin.label}: pending while it is open or under way, resolved once it is finished or set aside.`}</div>
			</div>

			<div class='actions'>
				<p7t-button ghost icon=${origin.icon} @click=${() => openTerminativeOrigin(terminative)}>Open ${origin.label}</p7t-button>
				<p7t-button
					icon=${terminative.ignored ? 'lucide:eye' : 'lucide:eye-off'}
					@click=${() => this.commitIgnored(!terminative.ignored)}>
					${terminative.ignored ? 'Stop ignoring' : 'Ignore'}
				</p7t-button>
			</div>
		`
	}

	private async commitIgnored(ignored: boolean) {
		const terminative = this.terminative
		if (!terminative) return

		const updated = await applyTerminativeUpdate(terminative, { ignored })
		if (!updated) return

		this.terminative = updated
		this.dispatchEvent(new CustomEvent<Terminative>('terminativechange', { detail: updated, bubbles: true, composed: true }))
	}
}

/** Hosts {@link TerminativeEditor} in a modal, relaying every persisted change to the opener. */
export class TerminativeModal extends ModalBase {
	constructor(
		private readonly terminative: Terminative,
		private readonly onChange?: (terminative: Terminative) => void,
	) {
		super()
	}

	override onOpen() {
		const editor = createChild(this.contentEl, 'p7t-terminative-editor')
		editor.terminative = this.terminative
		editor.addEventListener('terminativechange', e => this.onChange?.((e as CustomEvent<Terminative>).detail))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-terminative-editor': TerminativeEditor
	}
}
