import { Component, component, css, html, nothing, property, state } from "@a11d/lit"
import { AttentiveResolution, DirectiveTimeframeRecord, Timeframe, type Attentive, type AttentiveOccurrenceRef, type AttentiveUpdate } from "@pleiades/sdk"
import { App, Modal, Notice } from "obsidian"
import { core, openEntityEditor, SelectTimeframeModal, tooltip } from ".."
import type { TimeframeChoice } from "../editing/SelectTimeframeModal"
import type { EditablePart } from "../editing/EditableDataLink"
import type { EditableTimeUnit } from "../editing/EditableTimeUnit"

type Allocation = 'estimation' | 'minimum' | 'maximum'

/** Addresses an attentive for an update: a bound one by row id, an unbound one by its recurrence-id. */
export function attentiveOccurrence(attentive: Attentive): AttentiveOccurrenceRef {
	return attentive.polarisCycleId
		? { id: attentive.id }
		: { decreeId: attentive.decreeId, recurrenceDate: attentive.recurrenceDate, recurrenceTime: attentive.recurrenceTime }
}

/**
 * Editing surface for a single attentive — the attentive-side twin of the executive editor: its resolution
 * (done or pending), its timeframe affinity, and the estimation/minimum/maximum allocations. An attentive keeps
 * no tracked tally and its decree has no workflow status to shift, so those two columns are absent.
 *
 * Allocations are drafted locally so the bar follows an edit in progress, then persisted once committed; the
 * core reconciles the envelope, so its response is folded back over the draft.
 */
@component('p7t-attentive-editor')
export class AttentiveEditor extends Component {
	@property({
		type: Object,
		updated(this: AttentiveEditor, value: Attentive | undefined) {
			this.draft = { estimation: value?.estimation, minimum: value?.minimum, maximum: value?.maximum }
		}
	}) attentive?: Attentive

	@state() private draft: Record<Allocation, number | undefined> = { estimation: undefined, minimum: undefined, maximum: undefined }

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

			<p7t-allocation-bar
				.elapsed=${0}
				.estimation=${this.draft.estimation ?? 0}
				.minimum=${this.draft.minimum ?? 0}
				.maximum=${this.draft.maximum ?? 0}>
			</p7t-allocation-bar>

			<div class='allocations'>
				${this.allocationTemplate('minimum', 'Min')}
				${this.allocationTemplate('estimation', 'Estimated', true)}
				${this.allocationTemplate('maximum', 'Max')}
			</div>
		`
	}

	private allocationTemplate(allocation: Allocation, caption: string, featured = false) {
		return html`
			<div class='allocation ${featured ? 'featured' : ''}'>
				<p7t-editable-time-unit
					nullable
					?accent=${featured}
					.value=${this.draft[allocation]}
					@preview=${(e: CustomEvent<number>) => this.draft = { ...this.draft, [allocation]: e.detail }}
					@change=${(e: Event) => this.commitAllocation(allocation, e)}>
				</p7t-editable-time-unit>
				<span class='caption'>${caption}</span>
			</div>
		`
	}

	private commitAllocation(allocation: Allocation, e: Event) {
		const value = (e.target as EditableTimeUnit).value
		this.draft = { ...this.draft, [allocation]: value }
		void this.applyUpdate({ [allocation]: value ?? null } as AttentiveUpdate)
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
