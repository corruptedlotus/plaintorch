import { component, css, html, HTMLTemplateResult, nothing } from '@a11d/lit'
import { DecreeStatus, DirectiveStatus, FateStatus, LunarDirectiveStatus, ObjectiveStatus, type Directive } from '@pleiades/sdk'
import {
	getApp, IconName, LunarDirectiveModal, type ScheduleValue,
	SelectDirectiveStatusModal, SelectLunarDirectiveStatusModal, SelectObjectiveStatusModal
} from '..'
import {
	directiveActions, entityIcon, entityKindOf, isDirectiveKind,
	objectiveActions, saveEntityField, saveFateSchedule, type EditableField
} from './entityActions'
import { GridItemBase } from './GridItemBase'
import type { GridEntity } from './entityTree'

/**
 * One line of the backlog entity grid: a directive, or an incentive it owns.
 *
 * Everything structural — the indent lanes, the type notch, the editable title, and the trailing open-note/add
 * columns — is the shared {@link GridItemBase}. This variant only fills the two middle cells (a kind's measure and its
 * workflow state), names the icon each kind draws, offers the right creation actions, and routes an edited field to
 * the update its kind requires.
 */
@component('p7t-grid-item')
export class GridItem extends GridItemBase {
	static override get styles() {
		return css`
			${super.styles}

			.lane[data-guide='directive'] {
				border-inline-start-style: dashed;
				border-inline-start-color: color-mix(in srgb, var(--text-normal) 25%, transparent);
			}

			.lane[data-guide='incentive'] {
				border-inline-start-style: solid;
				border-inline-start-color: color-mix(in srgb, var(--text-normal) 45%, transparent);
			}

			/* The state icons are sized for a banner; a row wants them at text scale. */
			p7t-status-item::part(icon) {
				height: 1.4em;
				width: 1.4em;
			}
		`
	}

	protected override get kindIcon(): IconName {
		return entityIcon(this.row!.entity as GridEntity)
	}

	/**
	 * The first middle cell holds whatever quantity or schedule a kind carries, the second its workflow state.
	 * Splitting them this way is what the shared tracks are for: every kind's state lands in one column, so a run of
	 * mixed rows can be read straight down.
	 */
	protected override get middleCells(): (HTMLTemplateResult | typeof nothing)[] {
		return [this.measureCell, this.statusCell]
	}

	/** The kind's own quantity or schedule: an objective's Celestron, a declarative's timing. */
	protected get measureCell(): HTMLTemplateResult | typeof nothing {
		const entity = this.row!.entity as GridEntity
		switch (entityKindOf(entity)) {
			case 'objective':
				return html`<p7t-editable-starfire ${this.binder.bind('celestronValue')}></p7t-editable-starfire>`
			case 'fate': {
				// A fate is scheduled either way — a recurring Orbit or a fixed date/time — and the editable
				// lets the user switch and clears whichever it is not.
				const fate = entity as { orbit?: string, date?: string, startTime?: string }
				return html`<p7t-editable-schedule
					.orbit=${fate.orbit}
					.date=${fate.date}
					.time=${fate.startTime}
					@schedulechange=${(e: CustomEvent<ScheduleValue>) => saveFateSchedule(entity, e.detail)}>
				</p7t-editable-schedule>`
			}
			case 'decree':
				return html`<p7t-editable-orbit ${this.binder.bind('orbit')}></p7t-editable-orbit>`
			case 'lunar-directive':
				// A lunar directive carries no measure of its own, so its otherwise-empty column hosts the button
				// that opens its editing modal — the one place its timeframes are managed (PEP100 patch).
				return html`
					<p7t-button ghost icon='lucide:clock' label='Edit timeframes' @click=${() => this.openLunarEditor()}></p7t-button>
				`
			default:
				return nothing
		}
	}

	/** Opens the lunar directive's editing modal, where its timeframes are defined. */
	protected openLunarEditor() {
		new LunarDirectiveModal(getApp(), this.row!.entity as Directive).open()
	}

	/** The workflow state, for the kinds that carry a lifecycle worth shifting from here. */
	protected get statusCell(): HTMLTemplateResult | typeof nothing {
		const entity = this.row!.entity as GridEntity
		const kind = entityKindOf(entity)
		const prompt = kind === 'objective' ? SelectObjectiveStatusModal.prompt
			: kind === 'stellar-directive' ? SelectDirectiveStatusModal.prompt
			: kind === 'lunar-directive' ? SelectLunarDirectiveStatusModal.prompt
			: undefined

		return !prompt ? nothing : html`
			<p7t-editable .doEdit=${prompt} ${this.binder.bind('status')}>
				<p7t-status-item .status=${this.statusName}></p7t-status-item>
			</p7t-editable>
		`
	}

	/**
	 * The descriptor name of the entity's state.
	 *
	 * Every kind numbers its own state enum from zero, so the value alone is ambiguous — a directive's `2`
	 * is Active while an objective's is Onrush. The kind has to pick the enum.
	 */
	protected get statusName() {
		const status = (this.row!.entity as { status?: number }).status ?? 0
		switch (entityKindOf(this.row!.entity as GridEntity)) {
			case 'lunar-directive': return LunarDirectiveStatus[status] ?? 'OnHold'
			case 'stellar-directive': return DirectiveStatus[status] ?? 'Planned'
			case 'fate': return FateStatus[status] ?? 'Active'
			case 'decree': return DecreeStatus[status] ?? 'Active'
			default: return ObjectiveStatus[status] ?? 'Standby'
		}
	}

	/** What the row's add button offers, which depends on what the row holds. */
	protected override get actions() {
		const entity = this.row!.entity as GridEntity
		const kind = entityKindOf(entity)
		if (isDirectiveKind(kind)) {
			return directiveActions(entity as Directive)
		}

		return kind === 'objective' ? objectiveActions(entity.id) : []
	}

	protected override async persistField(keyPath: string): Promise<void> {
		await saveEntityField(this.row!.entity as GridEntity, keyPath as EditableField)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-grid-item': GridItem
	}
}
