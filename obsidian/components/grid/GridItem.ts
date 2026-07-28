import { Component, component, css, event, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import type { Directive } from '@pleiades/sdk'
import { EntityWatch, ExpandingAction, IconName, ReactiveBinder } from '..'
import { directiveActions, entityIcon, entityKindOf, isDirectiveKind, objectiveActions, openEntityNote, renameEntity } from './entityActions'
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

	protected readonly binder = new ReactiveBinder<{ title: string }>(this, 'boundEntity', {
		sourceUpdated: async () => {
			const entity = this.row?.entity
			if (!entity) {
				return
			}

			// The binding already wrote the new title into the canonical instance, so every other surface
			// showing this entity is told about it before the write is even sent.
			this.watch.publish()
			await renameEntity(entity, entity.title)
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
				--p7t-grid-lane-width: 1.5em;
			}

			:host(:hover) {
				background-color: color-mix(in srgb, var(--text-normal) 8%, transparent);
			}

			.lead {
				display: flex;
				align-items: stretch;
				padding-left: .5em;
			}

			/*
			 * One lane per ancestor level. The lining is a left border rather than a pseudo-element so a
			 * run of consecutive rows joins into one unbroken line down the group.
			 */
			.lane {
				flex: 0 0 var(--p7t-grid-lane-width);
				width: var(--p7t-grid-lane-width);
				border-left: 1px solid transparent;
			}

			.lane[data-guide='directive'] {
				border-left-style: dashed;
				border-left-color: color-mix(in srgb, var(--text-normal) 25%, transparent);
			}

			.lane[data-guide='incentive'] {
				border-left-style: solid;
				border-left-color: color-mix(in srgb, var(--text-normal) 45%, transparent);
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
				justify-content: flex-end;
				padding-inline: .2em;
				font-size: .9em;
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
	 * Empty for now: the tracks exist and stay aligned across every row, and what goes in them is still
	 * to be designed. Overriding this is how a variant fills them.
	 */
	protected get leadingCells(): (HTMLTemplateResult | typeof nothing)[] {
		return [nothing, nothing]
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
