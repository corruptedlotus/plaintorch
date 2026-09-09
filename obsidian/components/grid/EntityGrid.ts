import { component, html, HTMLTemplateResult } from '@a11d/lit'
import type { EntityTypeName } from '@pleiades/sdk'
import { core, DerivedRef, ExpandingAction } from '..'
import { creationActions } from './entityActions'
import { buildGridRows, type GridRow } from './entityTree'
import { GridBase } from './GridBase'

/**
 * The backlog grid: every directive and incentive as one flat run of rows.
 *
 * The shell — column tracks, expansion, the sticky FAB — is the shared {@link GridBase}. This variant sources the
 * directive and incentive listings, flattens them into rows, and draws each with the backlog row element.
 */
@component('p7t-entity-grid')
export class EntityGrid extends GridBase {
	private readonly directives = new DerivedRef(this, core.repos.directiveList)
	private readonly objectives = new DerivedRef(this, core.repos.objectiveList)
	private readonly fates = new DerivedRef(this, core.repos.fateList)
	private readonly decrees = new DerivedRef(this, core.repos.decreeList)

	protected override get observedKinds(): readonly EntityTypeName[] {
		// The tree is built from directives and the three incentives; reparenting a directive changes the tree's
		// shape without any listing changing, so the grid observes exactly those kinds — and nothing else.
		return ['StellarDirective', 'LunarDirective', 'Objective', 'Fate', 'Decree']
	}

	protected override get rows(): GridRow[] {
		return buildGridRows({
			directives: this.directives.value,
			objectives: this.objectives.value,
			fates: this.fates.value,
			decrees: this.decrees.value
		}, this.expandedKeys)
	}

	protected override get loading() {
		return !this.directives.value && !this.objectives.value && !this.fates.value && !this.decrees.value
	}

	protected override get emptyLabel(): string {
		return 'No directives or incentives yet'
	}

	protected override get fabLabel(): string {
		return 'Add an entity'
	}

	protected override get fabActions(): ExpandingAction[] {
		return creationActions()
	}

	protected override renderRow(row: GridRow): HTMLTemplateResult {
		return html`<p7t-grid-item .row=${row}></p7t-grid-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-grid': EntityGrid
	}
}
