import { Component, component, css, event, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import { DecreeStatus, DirectiveStatus, FateStatus, LunarDirectiveStatus, ObjectiveStatus, type Directive } from '@pleiades/sdk'
import {
	EntityWatch, ExpandingAction, getApp, IconName, LunarDirectiveModal, ReactiveBinder,
	SelectDirectiveStatusModal, SelectLunarDirectiveStatusModal, SelectObjectiveStatusModal
} from '..'
import {
	directiveActions, entityIcon, entityKindOf, isDirectiveKind, isSingleInstanceFate,
	objectiveActions, openEntityNote, saveEntityField, type EditableField
} from './entityActions'
import type { GridRow } from './entityTree'

/**
 * One line of the entity grid.
 *
 * Renders as a subgrid rather than as a self-contained row, so its cells sit on the column tracks of the
 * grid that owns it. That is what keeps every row's columns aligned while each row stays an independent
 * component that knows nothing about its neighbours.
 */
@component('p7t-grid-item')
export class GridItem extends Component {
	@property({ type: Object }) row?: GridRow

	/** Asks the grid to expand or collapse this row, identified by its key. */
	@event({ bubbles: true, composed: true }) requestRowToggle!: EventDispatcher<string>

	/**
	 * The entity arrives from the grid's listing and is canonical, so observing it is what makes a row
	 * follow an edit made in a banner elsewhere.
	 */
	protected readonly watch = new EntityWatch(this, () => this.row?.entity)

	protected readonly binder = new ReactiveBinder<Record<string, unknown>>(this, 'boundEntity', {
		sourceUpdated: async (_, keyPath) => {
			const entity = this.row?.entity
			if (!entity || !keyPath) {
				return
			}

			// The binding already wrote the value into the canonical instance, so every other surface
			// showing this entity is told about it before the write is even sent.
			this.watch.publish()
			await saveEntityField(entity, keyPath as EditableField)
		}
	})

	/** The binding target. Named apart from `row` so the binder writes into the entity, not the row. */
	protected get boundEntity() {
		return this.row?.entity
	}

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-columns: subgrid;
				grid-column: 1 / -1;
				align-items: stretch;
				font-family: var(--font-interface);
				border-radius: 12px;
				transition: background-color .3s ease;
				--p7t-grid-lane-width: 1em;
				font-size: 1.1em;
				padding-block: .2em;
				padding-inline-end: .2em;
			}

			:host(:hover) {
				background-color: color-mix(in srgb, var(--text-normal) 8%, transparent);
			}

			.lead {
				display: flex;
				align-items: stretch;
				padding-inline-start: .5em;
			}

			/*
			 * One lane per ancestor level. The lining is a left border rather than a pseudo-element so a
			 * run of consecutive rows joins into one unbroken line down the group.
			 */
			.lane {
				margin-inline-start: var(--p7t-grid-lane-width);
				flex: 0 0 var(--p7t-grid-lane-width);
				width: var(--p7t-grid-lane-width);
				border-inline-start: 2px solid transparent;
			}

			.lane[data-guide='directive'] {
				border-inline-start-style: dashed;
				border-inline-start-color: color-mix(in srgb, var(--text-normal) 25%, transparent);
			}

			.lane[data-guide='incentive'] {
				border-inline-start-style: solid;
				border-inline-start-color: color-mix(in srgb, var(--text-normal) 45%, transparent);
			}

			.notch {
				display: flex;
				align-items: center;
				justify-content: center;
				flex: 0 0 1.9em;
				width: 1.9em;
				padding-block: .4em;
				border-radius: 6px;
			}

			.notch p7t-icon {
				width: 1.3em;
				height: 1.3em;
			}

			.notch.expandable {
				cursor: pointer;
			}

			.notch.expandable:hover {
				background-color: color-mix(in srgb, var(--text-normal) 14%, transparent);
			}

			/*
			 * The type icon gives way to a chevron once the row is open, and while the row is hovered
			 * before it is — otherwise a collapsed directive gives no sign that it holds anything.
			 */
			.notch .chevron {
				display: none;
			}

			:host(:hover) .notch.expandable .kind,
			.notch.open .kind {
				display: none;
			}

			:host(:hover) .notch.expandable .chevron,
			.notch.open .chevron {
				display: block;
			}

			.title {
				display: flex;
				align-items: center;
				padding-inline: .5em;
				font-weight: 300;
				font-size: 1.05em;
				line-height: 1.1;
				min-width: 0;
			}

			p7t-editable-plaintext {
				justify-content: flex-start;
				text-align: start;
				min-width: 0;
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
			}

			.cell {
				display: flex;
				align-items: center;
				justify-content: stretch;
				padding-inline: .2em;
				font-size: .9em;
				white-space: nowrap;
			}

			/* The state icons are sized for a banner; a row wants them at text scale. */
			p7t-status-item::part(icon) {
				height: 1.4em;
				width: 1.4em;
			}

			p7t-editable-starfire p7t-icon {
				width: 1.2em;
				height: 1.2em;
			}

			/* Row-level affordances stay out of the way until the row is under the pointer. */
			.cell.actions {
				opacity: 0;
				transition: opacity .2s ease;
			}

			:host(:hover) .cell.actions,
			.cell.actions:focus-within {
				opacity: 1;
			}

			.goto {
				display: flex;
				align-items: center;
				justify-content: center;
				min-width: 1.9em;
				min-height: 1.9em;
				border: none;
				border-radius: 8px;
				background: transparent;
				color: inherit;
				cursor: pointer;
				transition: background-color .2s ease;
			}

			.goto:hover {
				background-color: color-mix(in srgb, var(--text-normal) 14%, transparent);
			}

			.goto p7t-icon {
				width: 1.2em;
				height: 1.2em;
			}

			.measure-button {
				display: flex;
				align-items: center;
				justify-content: center;
				min-width: 1.9em;
				min-height: 1.9em;
				border: none;
				border-radius: 8px;
				background: transparent;
				color: inherit;
				cursor: pointer;
				transition: background-color .2s ease;
			}

			.measure-button:hover {
				background-color: color-mix(in srgb, var(--text-normal) 14%, transparent);
			}

			.measure-button p7t-icon {
				width: 1.2em;
				height: 1.2em;
			}
		`
	}

	protected override get template() {
		const row = this.row
		if (!row) {
			return html``
		}

		return html`
			<div class='lead'>
				${row.guides.map(guide => html`<span class='lane' data-guide=${guide}></span>`)}
				<div
					class='notch ${row.expandable ? 'expandable' : ''} ${row.expanded ? 'open' : ''}'
					@click=${() => this.toggleExpansion()}>
					<p7t-icon class='kind' .icon=${this.kindIcon}></p7t-icon>
					${!row.expandable ? '' : html`
						<p7t-icon class='chevron' icon=${row.expanded ? 'lucide:chevron-down' : 'lucide:chevron-right'}></p7t-icon>
					`}
				</div>
			</div>
			<div class='title'>
				<p7t-editable-plaintext ${this.binder.bind('title')}></p7t-editable-plaintext>
			</div>
			${this.leadingCells.map(cell => html`<div class='cell'>${cell}</div>`)}
			<div class='cell actions'>
				<button class='goto' aria-label='Open note' @click=${() => this.open()}>
					<p7t-icon icon='lucide:square-arrow-out-up-right'></p7t-icon>
				</button>
			</div>
			<div class='cell actions'>
				${this.actions.length === 0 ? nothing : html`
					<p7t-expanding-actions .actions=${this.actions} actionLabel='Add'></p7t-expanding-actions>
				`}
			</div>
		`
	}

	/** The icon of the entity's own kind, shown while the row is closed. */
	protected get kindIcon(): IconName {
		return entityIcon(this.row!.entity)
	}

	/**
	 * The cells before the two trailing action columns.
	 *
	 * The first holds whatever quantity or schedule a kind carries, the second holds its workflow state.
	 * Splitting them this way is what the shared tracks are for: every kind's state lands in one column,
	 * so a run of mixed rows can be read straight down.
	 */
	protected get leadingCells(): (HTMLTemplateResult | typeof nothing)[] {
		return [this.measureCell, this.statusCell]
	}

	/** The kind's own quantity or schedule: an objective's Celestron, a declarative's timing. */
	protected get measureCell(): HTMLTemplateResult | typeof nothing {
		const entity = this.row!.entity
		switch (entityKindOf(entity)) {
			case 'objective':
				return html`<p7t-editable-starfire ${this.binder.bind('celestronValue')}></p7t-editable-starfire>`
			case 'fate':
				// A one-off fate is placed by its date; a recurring one by its Orbit definition.
				return isSingleInstanceFate(entity)
					? html`<p7t-editable-date ${this.binder.bind('date')}></p7t-editable-date>`
					: html`<p7t-editable-orbit ${this.binder.bind('orbit')}></p7t-editable-orbit>`
			case 'decree':
				return html`<p7t-editable-orbit ${this.binder.bind('orbit')}></p7t-editable-orbit>`
			case 'lunar-directive':
				// A lunar directive carries no measure of its own, so its otherwise-empty column hosts the button
				// that opens its editing modal — the one place its timeframes are managed (PEP100 patch).
				return html`
					<button class='measure-button' aria-label='Edit timeframes' @click=${() => this.openLunarEditor()}>
						<p7t-icon icon='lucide:clock'></p7t-icon>
					</button>
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
		const entity = this.row!.entity
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
		switch (entityKindOf(this.row!.entity)) {
			case 'lunar-directive': return LunarDirectiveStatus[status] ?? 'OnHold'
			case 'stellar-directive': return DirectiveStatus[status] ?? 'Planned'
			case 'fate': return FateStatus[status] ?? 'Active'
			case 'decree': return DecreeStatus[status] ?? 'Active'
			default: return ObjectiveStatus[status] ?? 'Standby'
		}
	}

	/** What the row's add button offers, which depends on what the row holds. */
	protected get actions(): ExpandingAction[] {
		const entity = this.row!.entity
		const kind = entityKindOf(entity)
		if (isDirectiveKind(kind)) {
			return directiveActions(entity as Directive)
		}

		return kind === 'objective' ? objectiveActions(entity.id) : []
	}

	protected toggleExpansion() {
		if (this.row?.expandable) {
			this.requestRowToggle.dispatch(this.row.key)
		}
	}

	protected async open() {
		await openEntityNote(this.row!.entity)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-grid-item': GridItem
	}
}
