import { component, html, HTMLTemplateResult } from '@a11d/lit'
import type { DataRecord } from '@3mo/data-grid/controller'
import type { EntityTypeName } from '@pleiades/sdk'
import { core, DerivedRef, ExpandingAction } from '..'
import { creationActions } from './entityActions'
import { buildEntityTree, type GridNode, type GridRow } from './entityTree'
import { GridBase } from './GridBase'
import { EntityGridColumn } from './GridItem'

/**
 * The backlog grid: every directive and incentive as one tree of rows.
 *
 * The shell — the columns and their heading row, expansion, the sticky FAB — is the shared {@link GridBase}. This
 * variant sources the directive and incentive listings, shapes them into a tree, names its columns, and draws each row
 * with the backlog row element.
 */
@component('p7t-entity-grid')
export class EntityGrid extends GridBase {
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
		// The tree is built from directives and the three incentives; reparenting a directive changes the tree's
		// shape without any listing changing, so the grid observes exactly those kinds — and nothing else.
		return ['StellarDirective', 'LunarDirective', 'Objective', 'Fate', 'Decree']
	}

	protected override buildTree(): GridNode[] {
		return buildEntityTree({
			directives: this.directives.value,
			objectives: this.objectives.value,
			fates: this.fates.value,
			decrees: this.decrees.value
		})
	}

	protected override get loading() {
		return !this.directives.value && !this.objectives.value && !this.fates.value && !this.decrees.value
	}

	protected override get layoutKey() {
		return 'backlog'
	}

	protected override get emptyLabel(): string {
		return 'No directives or incentives yet'
	}

	protected override get fabLabel(): string {
		return 'Create New'
	}

	protected override get fabActions(): ExpandingAction[] {
		return creationActions()
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
