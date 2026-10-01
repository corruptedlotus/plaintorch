import { component, html, HTMLTemplateResult } from '@a11d/lit'
import type { EntityTypeName } from '@pleiades/sdk'
import { core, DerivedRef, ExpandingAction } from '..'
import { creationActions } from './entityActions'
import { buildGridRows, type GridRow } from './entityTree'
import { GridBase } from './GridBase'

/**
 * The backlog grid: every stellar directive and incentive as one flat run of rows.
 *
 * The shell — column tracks, expansion, the sticky FAB — is the shared {@link GridBase}. This variant sources the
 * directive and incentive listings, flattens them into rows, and draws each with the backlog row element.
 *
 * Lunar directives, and the incentives they own, are left out: they live on the Moonlight page
 * (`<p7t-moonlight-view>`), which lays the lunar hierarchy out as cards instead.
 */
@component('p7t-entity-grid')
export class EntityGrid extends GridBase {
	private readonly directives = new DerivedRef(this, core.repos.directiveList)
	private readonly objectives = new DerivedRef(this, core.repos.objectiveList)
	private readonly fates = new DerivedRef(this, core.repos.fateList)
	private readonly decrees = new DerivedRef(this, core.repos.decreeList)

	protected override get observedKinds(): readonly EntityTypeName[] {
		// The tree is built from stellar directives and the three incentives; reparenting changes the tree's shape
		// without any listing changing, so the grid observes exactly those kinds — and nothing else.
		return ['StellarDirective', 'Objective', 'Fate', 'Decree']
	}

	protected override get rows(): GridRow[] {
		const directives = this.directives.value
		const lunarIds = new Set(directives?.filter(directive => directive.isLunar).map(directive => directive.id))
		// An incentive under a lunar directive is shown on the Moonlight page; dropping only its owner would surface it
		// here as a world incentive, which it is not.
		const outsideMoonlight = <T extends { directiveId?: string }>(incentives: readonly T[] | undefined) =>
			incentives?.filter(incentive => !incentive.directiveId || !lunarIds.has(incentive.directiveId))

		return buildGridRows({
			directives: directives?.filter(directive => !directive.isLunar),
			objectives: outsideMoonlight(this.objectives.value),
			fates: outsideMoonlight(this.fates.value),
			decrees: outsideMoonlight(this.decrees.value)
		}, this.expandedKeys)
	}

	protected override get loading() {
		return !this.directives.value && !this.objectives.value && !this.fates.value && !this.decrees.value
	}

	protected override get emptyLabel(): string {
		return 'No directives or incentives yet'
	}

	protected override get fabLabel(): string {
		return 'Create New'
	}

	protected override get fabActions(): ExpandingAction[] {
		return creationActions(undefined, ['stellar-directive', 'objective', 'fate', 'decree'])
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
