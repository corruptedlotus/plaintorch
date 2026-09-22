import { Component, component, css, html, nothing, property } from "@a11d/lit"
import { AttentiveResolution, DirectiveTimeframeRecord, Timeframe, type Attentive, type AttentiveOccurrenceRef, type AttentiveUpdate } from "@pleiades/sdk"
import { App, Modal, Notice } from "obsidian"
import { core, openEntityEditor, SelectTimeframeModal, tooltip } from ".."
import type { TimeframeChoice } from "../editing/SelectTimeframeModal"
import type { EditablePart } from "../editing/EditableDataLink"

/** Addresses an attentive for an update by its RECURRENCE-ID (attentives are always unbound, PEP111). */
export function attentiveOccurrence(attentive: Attentive): AttentiveOccurrenceRef {
	return { decreeId: attentive.decreeId, recurrenceDate: attentive.recurrenceDate, recurrenceTime: attentive.recurrenceTime }
}

/**
 * Editing surface for a single attentive — an unbound occurrence of its decree (PEP111): its resolution (done
 * or pending) and its timeframe affinity. An attentive carries no time allocation (that lives on the executive)
 * and its decree has no workflow status to shift, so neither column appears.
 */
@component('p7t-attentive-editor')
export class AttentiveEditor extends Component {
	@property({ type: Object }) attentive?: Attentive

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				gap: .8em;
				font-family: var(--font-interface);
				color: var(--text-normal);
				box-sizing: border-box;
				padding: 1em;
			}

			.header {
				display: flex;
				align-items: center;
				gap: .6ch;
				margin-block-end: .8em;
				margin-top: -1em;
			}

			.title {
				flex: 1;
				font-size: 1.6em;
				font-weight: 250;
				line-height: 1.1;
				color: color-mix(in srgb, var(--text-normal) 88%, transparent);
			}

			.open {
				flex: 0 0 auto;
				width: 1.3em;
				height: 1.3em;
				cursor: pointer;
				color: var(--p7t-flare-accent, var(--interactive-accent));
				opacity: .7;
				transition: .3s ease;

				&:hover {
					opacity: 1;
				}
			}

			.label {
				display: block;
				font-size: .8em;
				font-weight: 600;
				line-height: 1;
				color: var(--p7t-flare-accent, var(--interactive-accent));
				margin-block-end: .4em;
			}

			.status {
				display: flex;
				flex-direction: column;
				align-items: flex-start;
				gap: .5em;
			}

			.status > * {
				justify-content: flex-start;
				align-self: stretch;
			}

			.allocations {
				display: grid;
				font-size: 1.2em;
				grid-template-columns: 1fr auto 1fr;
				align-items: center;
				justify-items: center;
				gap: .4em;
			}

			.allocation {
				display: flex;
				flex-direction: column;
				align-items: center;
				gap: .1em;

				& p7t-editable-time-unit {
					font-size: 2.2em;
					font-weight: 300;
					line-height: 1;
				}

				& .caption {
					font-size: .75em;
					font-weight: 600;
					text-transform: uppercase;
					color: color-mix(in srgb, var(--text-normal) 65%, transparent);
				}
			}

			.allocation.featured {
				background-color: color-mix(in srgb, var(--text-normal) 5%, transparent);
				border-radius: 18px;
				padding: .7em 1.1em;

				& p7t-editable-time-unit {
					font-size: 2.6em;
				}

				& .caption {
					text-transform: none;
					font-weight: 500;
					color: var(--p7t-flare-accent, var(--interactive-accent));
				}
			}
		`
	}

	protected override get template() {
		const attentive = this.attentive
		if (!attentive) return html``

		const decree = attentive.decree
		const done = attentive.resolution === AttentiveResolution.Done
		return html`
			<div class='header'>
				<div class='title'>${decree?.title ?? 'Untitled Attentive'}</div>
				${!decree ? nothing : html`
					<p7t-icon class='open' icon='lucide:file-symlink' ${tooltip('Open decree')} @click=${() => openEntityEditor(decree)}></p7t-icon>
				`}
			</div>

			<div class='status'>
				<span class='label'>Status</span>

				<p7t-editable
					.value=${done}
					.doEdit=${(value: boolean | undefined) => Promise.resolve(!value)}
					@change=${(e: Event) => this.commitResolution(e)}>
					<p7t-executed-item small ?executed=${done}></p7t-executed-item>
				</p7t-editable>

				<p7t-editable
					.value=${attentive.affinityTimeframe}
					.doEdit=${SelectTimeframeModal.prompt}
					@change=${(e: Event) => this.commitAffinity(e)}>
					<p7t-timeframe-item small nullable affinity .timeframe=${attentive.affinityTimeframe}></p7t-timeframe-item>
				</p7t-editable>
			</div>
		`
	}

	private commitResolution(e: Event) {
		const done = (e.target as EditablePart<boolean>).value
		if (done === undefined) return
		void this.applyUpdate({ resolution: done ? AttentiveResolution.Done : AttentiveResolution.Pending })
	}

	private async commitAffinity(e: Event) {
		const choice = (e.target as EditablePart<TimeframeChoice>).value
		// A cancelled pick fires no change; a resolved one is a record (affine) or null (clear).
		if (choice === undefined) return

		const updated = await this.applyUpdate({ affinityTimeframeId: choice === null ? null : choice.id })
		if (updated && choice) {
			// The update response may not carry the navigation; fold the picked timeframe in for display.
			this.attentive = { ...this.attentive!, affinityTimeframe: AttentiveEditor.recordToTimeframe(choice) }
		}
	}

	private static recordToTimeframe(record: DirectiveTimeframeRecord): Timeframe {
		return {
			id: record.id,
			directiveId: record.directiveId,
			title: record.title,
			startTime: record.startTime,
			endTime: record.endTime,
			orbit: record.orbit,
			icon: record.icon,
			iconMedia: record.iconMedia,
			autoInclusion: record.autoInclusion,
			autoInclusionColleges: record.autoInclusionColleges,
		}
	}

	private async applyUpdate(update: AttentiveUpdate): Promise<Attentive | undefined> {
		const attentive = this.attentive
		if (!attentive) return undefined

		const updated = await core.declaratives.updateAttentive(attentiveOccurrence(attentive), update)
		if (!updated) {
			new Notice('Failed to update attentive.')
			return undefined
		}

		// The update response carries no decree navigation, so the known decree (and a known affinity the
		// response omits) is kept — otherwise an allocation edit would blank the header and the affinity chip.
		this.attentive = {
			...attentive,
			...updated,
			decree: updated.decree ?? attentive.decree,
			affinityTimeframe: updated.affinityTimeframe ?? (updated.affinityTimeframeId === attentive.affinityTimeframeId ? attentive.affinityTimeframe : undefined),
		}
		this.dispatchEvent(new CustomEvent<Attentive>('attentivechange', { detail: this.attentive, bubbles: true, composed: true }))
		return this.attentive
	}
}

/** Hosts {@link AttentiveEditor} inside an Obsidian modal, relaying every persisted change to the opener. */
export class AttentiveModal extends Modal {
	constructor(
		app: App,
		private readonly attentive: Attentive,
		private readonly onChange?: (attentive: Attentive) => void,
	) {
		super(app)
	}

	override onOpen() {
		const editor = this.contentEl.createEl('p7t-attentive-editor')
		editor.attentive = this.attentive
		editor.addEventListener('attentivechange', e => this.onChange?.((e as CustomEvent<Attentive>).detail))
	}

	override onClose() {
		this.contentEl.empty()
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-attentive-editor': AttentiveEditor
	}
}
