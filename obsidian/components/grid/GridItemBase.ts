import { Component, css, event, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import {
	ContextMenuController, entityContextMenu, EntityWatch, ExpandingAction, IconName, ReactiveBinder,
	type ContextMenuSpec, type InteractableEntity
} from '..'
import { openEntityNote } from './entityActions'
import type { GridRow } from './entityTree'

/**
 * The shared body of one grid line, for every grid variant.
 *
 * Renders as a subgrid rather than a self-contained row, so its cells sit on the column tracks of the grid that
 * owns it — that is what keeps every row's columns aligned while each row stays an independent component knowing
 * nothing about its neighbours. The lead (indent lanes + type notch), the editable title, the trailing open-note and
 * add columns, the right-click menu, and the canonical-entity watch are all shared here; a variant supplies only what
 * its middle cells hold ({@link middleCells}), what its add button offers ({@link actions}), which icon its kind draws
 * ({@link kindIcon}), and how an edited field is persisted ({@link persistField}).
 */
export abstract class GridItemBase extends Component {
	@property({ type: Object }) row?: GridRow

	/** Asks the owning grid to expand or collapse this row, identified by its key. */
	@event({ bubbles: true, composed: true }) requestRowToggle!: EventDispatcher<string>

	/**
	 * The entity arrives from the grid's listing and is canonical, so observing it is what makes a row follow an edit
	 * made in a banner elsewhere.
	 */
	protected readonly watch = new EntityWatch(this, () => this.row?.entity)

	protected readonly binder = new ReactiveBinder<Record<string, unknown>>(this, 'boundEntity', {
		sourceUpdated: async (_, keyPath) => {
			const entity = this.row?.entity
			if (!entity || !keyPath) {
				return
			}

			// The binding already wrote the value into the canonical instance, so every other surface showing this
			// entity is told about it before the write is even sent.
			this.watch.publish()
			await this.persistField(keyPath)
		}
	})

	/** The binding target. Named apart from `row` so the binder writes into the entity, not the row. */
	protected get boundEntity() {
		return this.row?.entity
	}

	/**
	 * Raises the row's entity context menu on right-click — opening its note, editing it in its banner, and deletion
	 * for the kinds the SDK can delete. As a controller it needs no template handler and withholds the menu on an
	 * empty row.
	 */
	protected readonly contextMenu = new ContextMenuController(this, (): ContextMenuSpec | undefined =>
		this.row?.entity ? entityContextMenu(this.row.entity as InteractableEntity) : undefined)

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

			/* A row on the active lore spine keeps a subtle accented wash so the current era/chapter/act/phase reads
			 * at a glance; hover deepens it like any other row. Variants that never mark a row active never see it. */
			:host([active]) {
				background-color: color-mix(in srgb, var(--interactive-accent) 10%, transparent);
			}

			:host([active]:hover) {
				background-color: color-mix(in srgb, var(--interactive-accent) 16%, transparent);
			}

			.lead {
				display: flex;
				align-items: stretch;
				padding-inline-start: .5em;
			}

			/*
			 * One lane per ancestor level. The lining is a left border rather than a pseudo-element so a run of
			 * consecutive rows joins into one unbroken line down the group.
			 */
			.lane {
				margin-inline-start: var(--p7t-grid-lane-width);
				flex: 0 0 var(--p7t-grid-lane-width);
				width: var(--p7t-grid-lane-width);
				border-inline-start: 2px solid transparent;
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
			 * The type icon gives way to a chevron once the row is open, and while the row is hovered before it is —
			 * otherwise a collapsed parent gives no sign that it holds anything.
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
		`
	}

	protected override updated() {
		// The active wash is driven by an attribute rather than a reactive property so a variant that never marks a row
		// active needs to opt into nothing; a plain row simply carries no attribute.
		this.toggleAttribute('active', !!this.row?.active)
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
			<div class='cell leading'>${this.leadingCell}</div>
			<div class='title'>
				<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
			</div>
			${this.middleCells.map(cell => html`<div class='cell'>${cell}</div>`)}
			<div class='cell actions'>
				<p7t-button ghost icon='lucide:square-arrow-out-up-right' label='Open note' @click=${() => this.open()}></p7t-button>
			</div>
			<div class='cell actions'>
				${this.actions.length === 0 ? nothing : html`
					<p7t-expanding-actions .actions=${this.actions} actionLabel='Add'></p7t-expanding-actions>
				`}
			</div>
		`
	}

	/** The icon of the entity's own kind, shown while the row is closed. */
	protected abstract get kindIcon(): IconName

	/** An optional label between the notch and the title (lore's level and index). Empty for variants without one. */
	protected get leadingCell(): HTMLTemplateResult | typeof nothing {
		return nothing
	}

	/**
	 * The cells between the title and the two trailing action columns. Every variant lands them on the same shared
	 * tracks, so a run of mixed rows can still be read straight down.
	 */
	protected get middleCells(): (HTMLTemplateResult | typeof nothing)[] {
		return []
	}

	/** What the row's add button offers, which depends on what the row holds. Empty hides the button. */
	protected get actions(): ExpandingAction[] {
		return []
	}

	/** Persists one edited field of the row's entity through whatever call its kind and field require. */
	protected abstract persistField(keyPath: string): Promise<void>

	protected toggleExpansion() {
		if (this.row?.expandable) {
			this.requestRowToggle.dispatch(this.row.key)
		}
	}

	/** Opens the note the row's entity is the authority for, in a new tab. */
	protected async open() {
		await openEntityNote(this.row!.entity)
	}
}
