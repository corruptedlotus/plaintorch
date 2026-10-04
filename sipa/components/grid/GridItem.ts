import { component, css, html, HTMLTemplateResult, live, nothing, property, PropertyValues } from '@a11d/lit'
import { Directive, Objective, OrbitUnit, typeNameOf, type DeclarativeCalendar } from '@pleiades/sdk'
import {
	IconName, type AnyStatus, type ScheduleValue,
	SelectStatusModal, objectiveStatusDescriptors, directiveStatusDescriptors, lunarDirectiveStatusDescriptors
} from '..'
import {
	directiveActions, entityIcon, entityKindLabel, entityKindOf, entityStatusName, isDirectiveKind, isInactiveEntity,
	objectiveActions, reparentEntity, reparentRefusal, saveDecreeSchedule, saveEntityField, saveFateSchedule, type EditableField
} from './entityActions'
import { GridItemBase } from './GridItemBase'
import type { GridEntity } from './entityTree'

/** The backlog grid's columns besides the title, by key. */
export enum EntityGridColumn {
	/** A kind's own quantity or schedule: an objective's Celestron, a declarative's timing. */
	Measure = 'measure',
	/** A kind's workflow state. */
	Status = 'status'
}

/**
 * One line of the backlog entity grid: a directive, or an incentive it owns.
 *
 * Everything structural — the indent lanes, the type notch, the editable title, and the trailing open-note/add
 * columns — is the shared {@link GridItemBase}. This variant only fills the two middle cells (a kind's measure and its
 * workflow state), names the icon each kind draws, offers the right creation actions, and routes an edited field to
 * the update its kind requires.
 *
 * Rows rearrange the tree by drag and drop: any row can be picked up by its kind icon, and dropping it on a
 * directive row moves it under that directive — a directive to a new parent, an incentive to a new owner. Dropping
 * it on the world heading takes it out from under its directive instead; that heading stands by even when the
 * world is empty ({@link GridRow.vacant}), out of sight until a drag could use it. Each
 * entity travels under its runtime type name, so an objective dragged from here is also welcome wherever else
 * objectives are taken (the Polaris card).
 */
@component('p7t-grid-item')
export class GridItem extends GridItemBase {
	@property({ type: Boolean, reflect: true }) root = false

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

			.lane[data-guide='world'] {
				border-inline-start-style: solid;
				border-inline-start-color: color-mix(in srgb, var(--interactive-accent) 80%, transparent);
			}

			/* The state icons are sized for a banner; a row wants them at text scale. */
			p7t-status-item::part(icon) {
				height: 1.4em;
				width: 1.4em;
			}

			:host([root]) {
				pointer-events: none;
				color: color-mix(in srgb, var(--interactive-accent) 80%, transparent);
				text-transform: uppercase;
			}

			/* The heading is inert until a drag could land on it — then it is the way out from under a directive. */
			:host([root][transfer-target]) {
				pointer-events: auto;
			}

			:host([root][vacant]:not([transfer-target])) {
				display: none;
			}

			.root .hint {
				display: none;
				margin-inline-start: 1em;
				font-size: .75em;
				text-transform: none;
				opacity: .7;
			}

			:host([transfer-target]) .root .hint {
				display: inline;
			}
			
			/* The heading names a group rather than filling columns, so it runs the whole width of the row. */
			.root {
				grid-column: 1 / -1;

				& .title {
					font-weight: 500;
					font-size: .85em;
				}
			}
		`
	}

	protected override get kindIcon(): IconName {
		return entityIcon(this.row!.entity as GridEntity)
	}

	protected override get kindName() {
		return entityKindLabel(entityKindOf(this.row!.entity as GridEntity))
	}

	/** A directive or incentive done with is dimmed; the world heading carries no entity, and so no state. */
	protected override get inactive() {
		return this.row?.kind !== 'root' && isInactiveEntity(this.row?.entity)
	}

	protected override get entityIcon() {
		const entity = this.row?.entity
		if (entity instanceof Directive) {
			return entity.iconMedia
		}

		return undefined
	}

	protected override transferKind(entity: { readonly id: string } | null) {
		return entity ? typeNameOf(entity) : undefined
	}

	protected override get acceptedTransferKinds() {
		return ['StellarDirective', 'LunarDirective', 'Objective', 'Fate', 'Decree']
	}

	/**
	 * The directive a drop on this row would move an entity under: the row's own, for a directive row; none — the
	 * top level — for the world heading. `false` for a row that takes nothing.
	 */
	private get transferParent(): Directive | undefined | false {
		if (this.row!.kind === 'root') {
			return undefined
		}

		const own = this.row!.entity as GridEntity
		return isDirectiveKind(entityKindOf(own)) ? own as Directive : false
	}

	/** A directive row and the world heading take what the tree's rules let them; every other row takes nothing. */
	protected override canTakeTransfer(entity: { readonly id: string }) {
		const parent = this.transferParent
		return parent !== false && reparentRefusal(entity as GridEntity, parent) === undefined
	}

	protected override async takeTransfer(entity: { readonly id: string }) {
		const parent = this.transferParent
		return parent !== false && await reparentEntity(entity as GridEntity, parent)
	}

	/**
	 * The measure column holds whatever quantity or schedule a kind carries, the status column its workflow state.
	 * Splitting them this way is what the shared tracks are for: every kind's state lands in one column, so a run of
	 * mixed rows can be read straight down.
	 */
	protected override cellContent(column: string): HTMLTemplateResult | typeof nothing {
		switch (column) {
			case EntityGridColumn.Measure: return this.measureCell
			case EntityGridColumn.Status: return this.statusCell
			default: return nothing
		}
	}

	/** The kind's own quantity or schedule: an objective's Celestron, a declarative's timing. */
	protected get measureCell(): HTMLTemplateResult | typeof nothing {
		const entity = this.row!.entity as GridEntity
		switch (entityKindOf(entity)) {
			case 'objective':
				return html`<p7t-editable-starfire ${this.binder.bind('celestronValue')}></p7t-editable-starfire>`
			case 'fate': {
				// A fate is orbit-only (PEP111): a recurrence, or a one-shot `Z{…}` literal the editable's bubble writes.
				const fate = entity as { orbit?: string, calendar?: DeclarativeCalendar }
				return html`<p7t-editable-schedule
					short
					.calendar=${fate.calendar}
					.orbit=${fate.orbit}
					@schedulechange=${(e: CustomEvent<ScheduleValue>) => saveFateSchedule(entity, e.detail)}>
				</p7t-editable-schedule>`
			}
			case 'decree': {
				// The same bubble as the banner's: a decree's one-shot takes no duration, and a reflecting decree's a whole day.
				const decree = entity as { orbit?: string, calendar?: DeclarativeCalendar, reflect?: boolean }
				return html`<p7t-editable-schedule
					short
					.calendar=${decree.calendar}
					.orbit=${live(decree.orbit)}
					.oneShotUnits=${decree.reflect ? [OrbitUnit.Day] : undefined}
					.oneShotDuration=${false}
					@schedulechange=${(e: CustomEvent<ScheduleValue>) => saveDecreeSchedule(entity, e.detail)}>
				</p7t-editable-schedule>`
			}
			default:
				return nothing
		}
	}

	/** The workflow state, for the kinds that carry a lifecycle worth shifting from here. */
	protected get statusCell(): HTMLTemplateResult | typeof nothing {
		const entity = this.row!.entity as GridEntity
		const kind = entityKindOf(entity)
		const descriptors = kind === 'objective' ? objectiveStatusDescriptors
			: kind === 'stellar-directive' ? directiveStatusDescriptors
			: kind === 'lunar-directive' ? lunarDirectiveStatusDescriptors
			: undefined
		const prompt = descriptors && (() => SelectStatusModal.prompt<AnyStatus>(descriptors))

		return !prompt ? nothing : html`
			<p7t-editable .doEdit=${prompt} ${this.binder.bind('status')}>
				<p7t-status-item .status=${this.statusName}></p7t-status-item>
			</p7t-editable>
		`
	}

	/** The descriptor name of the entity's state; see {@link entityStatusName}. */
	protected get statusName() {
		return entityStatusName(this.row!.entity as GridEntity)
	}

	/** What the row's add button offers, which depends on what the row holds. */
	protected override get actions() {
		const entity = this.row!.entity as GridEntity
		const kind = entityKindOf(entity)
		if (isDirectiveKind(kind)) {
			return directiveActions(entity as Directive)
		}

		return kind === 'objective' ? objectiveActions(entity as Objective) : []
	}

	protected override async persistField(keyPath: string): Promise<void> {
		await saveEntityField(this.row!.entity as GridEntity, keyPath as EditableField)
	}

	override get template() {
		return this.row?.kind !== 'root' ? super.template : html`
			<div class='cell lead root' role='gridcell'>
				<p7t-icon class='notch' icon='lucide:astroid'></p7t-icon>
				<div class='leading'>${this.leadingCell}</div>
				<span class='title'>World Quests & Events</span>
				<span class='hint'>Drop here to take it out of its directive</span>
			</div>
		`
	}

	protected override updated() {
		super.updated()
		this.toggleAttribute('vacant', !!this.row?.vacant)
	}

	protected override firstUpdated(props: PropertyValues): void {
		if (this.row?.kind === 'root') {
			this.root = true
		}
		super.firstUpdated(props)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-grid-item': GridItem
	}
}
