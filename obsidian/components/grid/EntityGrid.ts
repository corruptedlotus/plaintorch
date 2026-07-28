import { Component, component, css, eventListener, html, nothing, repeat, state } from '@a11d/lit'
import type { EntitySubscription } from '@pleiades/sdk'
import { core, DerivedRef } from '..'
import { creationActions } from './entityActions'
import { buildGridRows, type GridRow } from './entityTree'

/**
 * The entity grid: every directive and incentive as one flat run of rows.
 *
 * Owns the column tracks. Each row is an independent component that subgrids onto these tracks, so the
 * columns stay aligned across the whole list without any row knowing what the others contain.
 */
@component('p7t-entity-grid')
export class EntityGrid extends Component {
	@state() private expandedKeys: ReadonlySet<string> = new Set()

	private readonly directives = new DerivedRef(this, core.repos.directiveList)
	private readonly objectives = new DerivedRef(this, core.repos.objectiveList)
	private readonly fates = new DerivedRef(this, core.repos.fateList)
	private readonly decrees = new DerivedRef(this, core.repos.decreeList)

	private storeSubscription?: EntitySubscription

	static override get styles() {
		return css`
			:host {
				position: relative;
				display: grid;
				/*
				 * The shared tracks. The leading track is sized by the deepest row's indentation, so it
				 * grows with nesting while everything after it stays put. The trailing tracks are
				 * placeholders until their content is designed — they collapse to nothing while empty.
				 */
				grid-template-columns: auto minmax(0, 1fr) auto auto auto auto;
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
			 * Sticks to the corner of the scrolling area rather than to the document, so it stays reachable
			 * however far down the list runs. It sits in the last row's track so it never overlaps a row.
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
		// The tree's shape comes from parent references on the entities themselves, so it can change
		// without any listing changing: reparenting a directive leaves every membership intact. No
		// per-entity subscription would report that, which is why this observes the store as a whole.
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

	protected get rows(): GridRow[] {
		return buildGridRows({
			directives: this.directives.value,
			objectives: this.objectives.value,
			fates: this.fates.value,
			decrees: this.decrees.value
		}, this.expandedKeys)
	}

	protected get loading() {
		return !this.directives.value && !this.objectives.value && !this.fates.value && !this.decrees.value
	}

	protected override get template() {
		const rows = this.rows
		return html`
			${rows.length > 0 ? nothing : html`
				<div class='notice'>${this.loading ? 'Loading…' : 'No directives or incentives yet'}</div>
			`}
			${/*
				Keyed by entity so a row keeps its element — and therefore its subscription — as the tree
				is expanded and collapsed around it.
			*/ repeat(rows, row => row.key, row => html`
				<p7t-grid-item .row=${row}></p7t-grid-item>
			`)}
			<p7t-expanding-actions
				class='fab'
				large
				actionLabel='Add an entity'
				.actions=${creationActions()}>
			</p7t-expanding-actions>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-grid': EntityGrid
	}
}
