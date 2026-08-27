import { Component, component, css, html, nothing, property, state } from "@a11d/lit"
import { DirectiveTimeframeRecord, Executive, ExecutiveUpdate, ObjectiveStatus, Timeframe } from "@pleiades/sdk"
import { App, Modal, Notice } from "obsidian"
import { core, SelectObjectiveStatusModal, SelectTimeframeModal } from ".."
import type { TimeframeChoice } from "../editing/SelectTimeframeModal"
import type { EditablePart } from "../editing/EditableDataLink"
import type { EditableTimeUnit } from "../editing/EditableTimeUnit"

type Allocation = 'elapsed' | 'estimation' | 'minimum' | 'maximum'

/**
 * Editing surface for a single executive: its tracked (elapsed) time, its executed flag, the
 * workflow status of the objective behind it, and the estimation/minimum/maximum allocations.
 *
 * Allocations are drafted locally so the bar can follow an edit in progress, then persisted once the
 * edit is committed. The server reconciles the allocation envelope, so the response is treated as
 * authoritative and folded back over the draft.
 */
@component('p7t-executive-editor')
export class ExecutiveEditor extends Component {
	@property({
		type: Object,
		updated(this: ExecutiveEditor, value: Executive | undefined) {
			// The allocations are nullable and kept undefined when unset, so a cleared allocation reads as cleared
			// rather than as zero; only the tracked tally always has a value.
			this.draft = {
				elapsed: value?.elapsed ?? 0,
				estimation: value?.estimation,
				minimum: value?.minimum,
				maximum: value?.maximum,
			}
		}
	}) executive?: Executive

	@state() private draft: Record<Allocation, number | undefined> = { elapsed: 0, estimation: undefined, minimum: undefined, maximum: undefined }

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
				align-items: flex-start;
				gap: 1em;
				margin-block-end: .8em;
			}

			.title {
				flex: 1;
				font-size: 1.6em;
				font-weight: 250;
				line-height: 1.1;
				margin-inline-end: .6em;
				margin-top: -1em;
				color: color-mix(in srgb, var(--text-normal) 88%, transparent);
			}

			.close {
				flex: 0 0 auto;
				width: 1.5em;
				height: 1.5em;
				padding: .15em;
				margin-block-start: .1em;
				border-radius: 6px;
				cursor: pointer;
				color: color-mix(in srgb, var(--text-normal) 45%, transparent);
				transition: .3s ease;

				&:hover {
					color: var(--text-normal);
					background-color: color-mix(in srgb, var(--text-normal) 12%, transparent);
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

			.columns {
				display: grid;
				grid-template-columns: 1fr 1fr;
				gap: 1.2em;
				align-items: start;
			}

			.tracked p7t-editable-time-unit {
				font-size: 3.2em;
				font-weight: 300;
				line-height: 1;
			}

			.status {
				border-inline-start: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				padding-inline-start: 1.2em;
				display: flex;
				flex-direction: column;
				align-items: flex-start;
				gap: .5em;
			}

			/* Both indicators edit in place, so they align to the start rather than centring. */
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
		const executive = this.executive
		if (!executive) return html``

		const objective = executive.objective

		return html`
			<div class='header'>
				<div class='title'>${objective?.title ?? executive.title ?? 'Untitled Executive'}</div>
			</div>

			<div class='columns'>
				<div class='tracked'>
					<span class='label'>Tracked</span>
					<p7t-editable-time-unit
						accent
						.value=${this.draft.elapsed}
						@preview=${(e: CustomEvent<number>) => this.previewAllocation('elapsed', e.detail)}
						@change=${(e: Event) => this.commitAllocation('elapsed', e)}>
					</p7t-editable-time-unit>
				</div>

				<div class='status'>
					<span class='label'>Status</span>
					<p7t-editable
						.value=${executive.executed}
						.doEdit=${(executed: boolean | undefined) => Promise.resolve(!executed)}
						@change=${(e: Event) => this.commitExecuted(e)}>
						<p7t-executed-item ?executed=${executive.executed}></p7t-executed-item>
					</p7t-editable>
					${!objective ? nothing : html`
						<p7t-editable
							.value=${objective.status}
							.doEdit=${SelectObjectiveStatusModal.prompt}
							@change=${(e: Event) => this.commitStatus(e)}>
							<p7t-status-item
								.status=${ObjectiveStatus[objective.status] as keyof typeof ObjectiveStatus}>
							</p7t-status-item>
						</p7t-editable>
					`}

					<p7t-editable
						.value=${executive.affinityTimeframe}
						.doEdit=${SelectTimeframeModal.prompt}
						@change=${(e: Event) => this.commitAffinity(e)}>
						<p7t-timeframe-item nullable .timeframe=${executive.affinityTimeframe}></p7t-timeframe-item>
					</p7t-editable>
				</div>
			</div>

			<p7t-allocation-bar
				.elapsed=${this.draft.elapsed ?? 0}
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
					@preview=${(e: CustomEvent<number>) => this.previewAllocation(allocation, e.detail)}
					@change=${(e: Event) => this.commitAllocation(allocation, e)}>
				</p7t-editable-time-unit>
				<span class='caption'>${caption}</span>
			</div>
		`
	}

	private previewAllocation(allocation: Allocation, value: number | undefined) {
		this.draft = { ...this.draft, [allocation]: value }
	}

	private commitAllocation(allocation: Allocation, e: Event) {
		const value = (e.target as EditableTimeUnit).value
		this.previewAllocation(allocation, value)
		// A cleared allocation commits as null (a canonical clear); a set one as its value.
		this.applyUpdate({ [allocation]: value ?? null } as ExecutiveUpdate)
	}

	private commitExecuted(e: Event) {
		const executed = (e.target as EditablePart<boolean>).value
		if (executed === undefined || executed === this.executive?.executed) return

		this.applyUpdate({ executed })
	}

	private async commitAffinity(e: Event) {
		const choice = (e.target as EditablePart<TimeframeChoice>).value
		// A cancelled pick fires no change; a resolved one is a record (affine) or null (clear).
		if (choice === undefined) return

		const update: ExecutiveUpdate = choice === null
			? { affinityTimeframeId: null }
			: { affinityTimeframeId: choice.id }

		const updated = await core.polaris.updateExecutive(this.executive!.id, update)
		if (!updated) {
			new Notice('Failed to update executive affinity.')
			return
		}

		// The update response carries no navigation properties, so fold the picked timeframe in for display.
		this.executive = {
			...this.executive!,
			...updated,
			objective: updated.objective ?? this.executive!.objective,
			affinityTimeframe: choice ? ExecutiveEditor.recordToTimeframe(choice) : undefined,
		}
		this.notifyChange()
	}

	/** Maps a global timeframe record to the {@link Timeframe} shape the executive carries, for immediate display. */
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

	private async commitStatus(e: Event) {
		const status = (e.target as EditablePart<ObjectiveStatus>).value
		const objective = this.executive?.objective
		if (status === undefined || !objective) return

		const updated = await core.objectives.shiftWorkflow(objective.id, { status })
		if (!updated) {
			new Notice('Failed to update objective status.')
			return
		}

		this.executive = { ...this.executive!, objective: updated }
		this.notifyChange()
	}

	private async applyUpdate(update: ExecutiveUpdate) {
		const executive = this.executive
		if (!executive) return

		const updated = await core.polaris.updateExecutive(executive.id, update)
		if (!updated) {
			new Notice('Failed to update executive.')
			return
		}

		// The update response carries no navigation properties, so the known objective and affinity timeframe are
		// kept — otherwise a plain allocation edit would spread `undefined` over them and drop the affinity display.
		this.executive = {
			...executive,
			...updated,
			objective: updated.objective ?? executive.objective,
			affinityTimeframe: updated.affinityTimeframe ?? executive.affinityTimeframe,
		}
		this.notifyChange()
	}

	private requestClose() {
		this.dispatchEvent(new CustomEvent<void>('requestclose', { bubbles: true, composed: true }))
	}

	private notifyChange() {
		this.dispatchEvent(new CustomEvent<Executive>('executivechange', {
			detail: this.executive!,
			bubbles: true,
			composed: true,
		}))
	}
}

/**
 * Hosts {@link ExecutiveEditor} inside an Obsidian modal, relaying every persisted change to the opener.
 */
export class ExecutiveModal extends Modal {
	constructor(
		app: App,
		private readonly executive: Executive,
		private readonly onChange?: (executive: Executive) => void,
	) {
		super(app)
	}

	override onOpen() {
		const editor = this.contentEl.createEl('p7t-executive-editor')
		editor.executive = this.executive
		editor.addEventListener('executivechange', e => this.onChange?.((e as CustomEvent<Executive>).detail))
		editor.addEventListener('requestclose', () => this.close())
	}

	override onClose() {
		this.contentEl.empty()
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-executive-editor': ExecutiveEditor
	}
}
