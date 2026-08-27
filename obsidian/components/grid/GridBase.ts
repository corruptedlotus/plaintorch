import { Component, css, eventListener, html, HTMLTemplateResult, nothing, repeat, state } from '@a11d/lit'
import type { EntitySubscription } from '@pleiades/sdk'
import { core, ExpandingAction } from '..'
import type { GridRow } from './entityTree'

/**
 * The shared shell every grid variant renders into.
 *
 * Owns the column tracks, so the cells of every row — whatever variant — line up down the whole list; owns the
 * expand/collapse state and the sticky creation FAB; and rebuilds when the store changes, since a tree's shape comes
 * from the entities' own references and can change without any listing changing. A variant supplies its {@link rows}
 * (already flattened, indented, and lined via each row's guides), its {@link loading} signal, the element it draws a
 * row with ({@link renderRow}), and what the FAB offers ({@link fabActions}).
 */
export abstract class GridBase extends Component {
	@state() protected expandedKeys: ReadonlySet<string> = new Set()

	private storeSubscription?: EntitySubscription

	static override get styles() {
		return css`
			:host {
				position: relative;
				display: grid;
				/*
				 * The shared tracks: indentation, title, two middle cells, then the two action columns.
				 *
				 * The leading track is sized by the deepest row's indentation, so it grows with nesting while
				 * everything after it stays put. The title keeps a floor rather than taking whatever is left: the
				 * trailing tracks size to their content, so an unbounded title track is the one that collapses in a
				 * narrow pane, and it is the cell that matters most. Below the floor the grid scrolls sideways instead.
				 */
				grid-template-columns: auto minmax(7em, 1fr) auto auto auto auto;
				align-content: start;
				overflow: auto;
				padding-block: .4em;
			}

			.notice {
				grid-column: 1 / -1;
				padding: 1em;
				opacity: .5;
				font-family: var(--font-interface);
				font-weight: 300;
			}

			/*
			 * Sticks to the corner of the scrolling area rather than to the document, so it stays reachable however
			 * far down the list runs. It sits in the last row's track so it never overlaps a row.
			 */
			.fab {
				position: sticky;
				grid-column: 1 / -1;
				justify-self: end;
				bottom: 1em;
				margin: .5em 1em 0 0;
				z-index: 5;
			}
		`
	}

	protected override connected() {
		// The tree's shape comes from parent references on the entities themselves, so it can change without any
		// listing changing: reparenting leaves every membership intact. No per-entity subscription would report that,
		// which is why this observes the store as a whole.
		this.storeSubscription = core.store.subscribeAll(() => this.requestUpdate())
	}

	protected override disconnected() {
		this.storeSubscription?.()
		this.storeSubscription = undefined
	}

	@eventListener('requestRowToggle')
	protected onRequestRowToggle(e: CustomEvent<string>) {
		e.stopPropagation()
		this.toggleRow(e.detail)
	}

	/** Expands or collapses one row. */
	public toggleRow(key: string) {
		const next = new Set(this.expandedKeys)
		if (!next.delete(key)) {
			next.add(key)
		}

		this.expandedKeys = next
	}

	protected override get template() {
		const rows = this.rows
		return html`
			${rows.length > 0 ? nothing : html`
				<div class='notice'>${this.loading ? 'Loading…' : this.emptyLabel}</div>
			`}
			${/*
				Keyed by row so a row keeps its element — and therefore its subscription — as the tree is expanded and
				collapsed around it.
			*/ repeat(rows, row => row.key, row => this.renderRow(row))}
			${this.fabActions.length === 0 ? nothing : html`
				<p7t-expanding-actions
					class='fab'
					large
					actionLabel=${this.fabLabel}
					.actions=${this.fabActions}>
				</p7t-expanding-actions>
			`}
		`
	}

	/** The flattened rows to render, in order. */
	protected abstract get rows(): GridRow[]

	/** Whether the listings the rows are built from have not arrived yet. */
	protected abstract get loading(): boolean

	/** Renders one row with the variant's own row element. */
	protected abstract renderRow(row: GridRow): HTMLTemplateResult

	/** What the creation FAB offers. Empty hides the FAB. */
	protected get fabActions(): ExpandingAction[] {
		return []
	}

	/** The FAB's trigger label. */
	protected get fabLabel(): string {
		return 'Add'
	}

	/** What the notice reads when there is nothing to show and nothing loading. */
	protected get emptyLabel(): string {
		return 'Nothing to show'
	}
}
