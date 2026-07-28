import { Component, component, css, event, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import { DecreeStatus, DirectiveStatus, FateStatus, LunarDirectiveStatus, ObjectiveStatus } from '@pleiades/sdk'
import { EntityWatch, IconName, navigateToEntity, statusDescriptors } from '..'
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

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-columns: subgrid;
				grid-column: 1 / -1;
				align-items: stretch;
				font-family: var(--font-interface);
				--p7t-grid-lane-width: 1.5em;
			}

			:host(:hover) {
				background-color: color-mix(in srgb, var(--text-normal) 6%, transparent);
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
				flex: 0 0 1.8em;
				width: 1.8em;
				padding-block: .35em;
			}

			.notch p7t-icon {
				width: 1.25em;
				height: 1.25em;
			}

			.expander {
				cursor: pointer;
				border-radius: 4px;
				transition: transform .2s ease, background-color .2s ease;
			}

			.expander:hover {
				background-color: color-mix(in srgb, var(--text-normal) 12%, transparent);
			}

			:host([data-expanded]) .expander {
				transform: rotate(0deg);
			}

			.expander:not(.open) {
				transform: rotate(-90deg);
			}

			.title {
				display: flex;
				align-items: center;
				gap: .4em;
				padding-inline: .5em;
				font-weight: 300;
				font-size: 1.05em;
				line-height: 1.1;
				min-width: 0;
			}

			.title span {
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
				cursor: pointer;
			}

			.cell {
				display: flex;
				align-items: center;
				justify-content: flex-end;
				padding-inline: .4em;
				font-size: .9em;
				opacity: .75;
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
				<div class='notch'>${this.notch}</div>
			</div>
			<div class='title'>
				<span @click=${() => this.navigate()}>${row.entity.title}</span>
			</div>
			${this.cells.map(cell => html`<div class='cell'>${cell}</div>`)}
		`
	}

	/**
	 * The leading marker: an expander when the row has children, otherwise the entity's state.
	 */
	protected get notch(): HTMLTemplateResult {
		const row = this.row!
		return !row.expandable
			? html`<p7t-icon icon=${this.statusIcon}></p7t-icon>`
			: html`
				<p7t-icon
					class='expander ${row.expanded ? 'open' : ''}'
					icon='lucide:chevron-down'
					@click=${() => this.requestRowToggle.dispatch(row.key)}>
				</p7t-icon>
			`
	}

	/**
	 * The trailing cells, in column order.
	 *
	 * Empty for now: the column tracks exist and stay aligned across every row, and what goes in them is
	 * still to be designed. Overriding this is how a variant fills them.
	 */
	protected get cells(): (HTMLTemplateResult | typeof nothing)[] {
		return [nothing, nothing, nothing, nothing]
	}

	protected get statusIcon(): IconName {
		const entity = this.row!.entity as { status?: number, $type?: string }
		const name = statusName(entity)
		return statusDescriptors[name as keyof typeof statusDescriptors]?.icon ?? 'state-zero'
	}

	protected navigate() {
		navigateToEntity(this.row!.entity.id)
	}
}

/**
 * Resolves the descriptor name of an entity's state.
 *
 * Every kind numbers its own state enum from zero, so the value alone is ambiguous — a directive's `2` is
 * Active while an objective's is Onrush. The kind has to pick the enum.
 */
function statusName(entity: { status?: number, $type?: string }): string {
	const status = entity.status ?? 0
	switch (entity.$type) {
		case 'lunar': return LunarDirectiveStatus[status] ?? 'OnHold'
		case 'stellar': return DirectiveStatus[status] ?? 'Planned'
		case 'fate': return FateStatus[status] ?? 'Active'
		case 'decree': return DecreeStatus[status] ?? 'Active'
		default: return ObjectiveStatus[status] ?? 'Standby'
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-grid-item': GridItem
	}
}
