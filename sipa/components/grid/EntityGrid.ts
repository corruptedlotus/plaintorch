import { component, html, HTMLTemplateResult, property } from '@a11d/lit'
import type { DataRecord } from '@3mo/data-grid/controller'
import type { EntityTypeName } from '@pleiades/sdk'
import { core, DerivedRef, ExpandingAction } from '..'
import { creationActions } from './entityActions'
import { buildEntityTree, stellarBacklogSource, type GridCriteria, type GridNode, type GridRow } from './entityTree'
import { GridBase } from './GridBase'
import { EntityGridColumn } from './GridItem'

/**
 * The backlog grid: every stellar directive and incentive as one tree of rows.
 *
 * The shell — the columns and their heading row, expansion, the sticky FAB — is the shared {@link GridBase}. This
 * variant sources the directive and incentive listings, shapes them into a tree, names its columns, and draws each row
 * with the backlog row element.
 *
 * Lunar directives, and the incentives they own, are left out: they live on the Moonlight page
 * (`<p7t-moonlight-view>`), which lays the lunar hierarchy out as cards instead.
 *
 * Given {@link criteria}, the grid shows only what passes them, each result inside its hierarchy, and opens its rows
 * while a search is on. The backlog page hands it the criteria of its search box.
 */
@component('p7t-entity-grid')
export class EntityGrid extends GridBase {
	/** What the grid shows of its tree; everything when unset. */
	@property({ attribute: false }) criteria?: GridCriteria

	private readonly directives = new DerivedRef(this, core.repos.directiveList)
	private readonly objectives = new DerivedRef(this, core.repos.objectiveList)
	private readonly fates = new DerivedRef(this, core.repos.fateList)
	private readonly decrees = new DerivedRef(this, core.repos.decreeList)

	protected override readonly columns = [
		GridBase.titleColumnDefinition(),
		GridBase.column(EntityGridColumn.Measure, 'Measure'),
		GridBase.column(EntityGridColumn.Status, 'Status')
	]

	protected override get observedKinds(): readonly EntityTypeName[] {
		// The tree is built from stellar directives and the three incentives; reparenting changes the tree's shape
		// without any listing changing, so the grid observes exactly those kinds — and nothing else.
		return ['StellarDirective', 'Objective', 'Fate', 'Decree']
	}

	/** The stellar backlog the listings hold, before any criteria. */
	private get source() {
		return stellarBacklogSource({
			directives: this.directives.value,
			objectives: this.objectives.value,
			fates: this.fates.value,
			decrees: this.decrees.value
		})
	}

	protected override buildTree(): GridNode[] {
		const criteria = this.criteria
		return buildEntityTree(this.source, criteria && (entity => criteria.matches(entity)))
	}

	protected override get searching() {
		return !!this.criteria?.active
	}

	protected override get loading() {
		return !this.directives.value && !this.objectives.value && !this.fates.value && !this.decrees.value
	}

	protected override get layoutKey() {
		return 'backlog'
	}

	/** Why nothing shows: a search that found nothing, defaults that hide everything there is, or an empty backlog. */
	protected override get emptyLabel(): string {
		if (this.criteria?.active) {
			return 'Nothing matches the search'
		}

		const { directives, objectives, fates, decrees } = this.source
		const held = [directives, objectives, fates, decrees].some(listing => (listing?.length ?? 0) > 0)
		return held ? 'Everything here is archived — search status:all to see it' : 'No directives or incentives yet'
	}

	protected override get fabLabel(): string {
		return 'Create New'
	}

	protected override get fabActions(): ExpandingAction[] {
		return creationActions(undefined, ['stellar-directive', 'objective', 'fate', 'decree'])
	}

	protected override renderRow(row: GridRow, record: DataRecord<GridNode>): HTMLTemplateResult {
		return html`<p7t-grid-item .row=${row} ${this.grid.row(record)}></p7t-grid-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-grid': EntityGrid
	}
}
