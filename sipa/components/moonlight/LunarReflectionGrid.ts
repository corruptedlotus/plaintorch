import { component, css, html, HTMLTemplateResult } from '@a11d/lit'
import type { DataRecord } from '@3mo/data-grid/controller'
import type { EntityTypeName } from '@pleiades/sdk'
import { core, DerivedRef } from '..'
import { gridRowKey, type GridNode, type GridRow } from '../grid/entityTree'
import { GridBase } from '../grid/GridBase'
import { LunarReflectionGridColumn, type LunarReflectionRow } from './LunarReflectionGridItem'
import { buildLunarReflection } from './moonlightTree'

/**
 * The decrees taking part in Lunar Reflection, as a grid: each decree's title, the directive that owns it, and its
 * humanised orbit. Stellar and top-level decrees that reflect are listed beside the lunar ones.
 *
 * A flat variant of the shared {@link GridBase}, so it carries the same sticky heading row as the backlog and lore —
 * columns that resize, fit, move and hide, remembered per grid — over rows that never nest. Which decrees appear is
 * {@link buildLunarReflection}'s call; each node carries its owning directive along to the row.
 *
 * Unlike the backlog it sits on the page as a card, the way the Moonlight cards above it do: the card's surface, its
 * heading row drawn on that same surface, and rows parted by hairlines.
 */
@component('p7t-lunar-reflection-grid')
export class LunarReflectionGrid extends GridBase {
	private readonly directives = new DerivedRef(this, core.repos.directiveList)
	private readonly decrees = new DerivedRef(this, core.repos.decreeList)

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding: .7em .4em;
				border-radius: 16px;
				background-color: var(--background-primary-alt);
				/* The sticky heading row still needs a surface for rows to pass under; the card's own keeps it unseen. */
				--p7t-grid-header-background: var(--background-primary-alt);
			}

			.header {
				color: var(--text-faint, var(--text-muted));
				border-block-end-color: color-mix(in srgb, var(--text-normal) 6%, transparent);
			}

			p7t-lunar-reflection-grid-item + p7t-lunar-reflection-grid-item {
				box-shadow: 0 -1px 0 color-mix(in srgb, var(--text-normal) 6%, transparent);
			}
		`
	}

	/**
	 * The directive and orbit columns flex with the title rather than sizing to their content, as the backlog's do: a
	 * flat list of three readings is easier to scan with them spread across the width than packed at its far edge.
	 */
	protected override readonly columns = [
		GridBase.titleColumnDefinition('Decree'),
		GridBase.column(LunarReflectionGridColumn.Directive, 'Directive', { width: 'minmax(9em, .8fr)' }),
		GridBase.column(LunarReflectionGridColumn.Orbit, 'Orbit', { width: 'minmax(12em, 1.2fr)' })
	]

	protected override get observedKinds(): readonly EntityTypeName[] {
		// Taking part turns on a decree's own flag and state, and on the states of the lunar directives on its lineage —
		// none of which changes a listing. That lineage is walked through stellar directives too, so both kinds count.
		return ['LunarDirective', 'StellarDirective', 'Decree']
	}

	protected override buildTree(): GridNode[] {
		return buildLunarReflection({ directives: this.directives.value, decrees: this.decrees.value })
			.map(({ decree, directive }) => ({ id: gridRowKey(decree), entity: decree, kind: 'decree', guides: [], directive }))
	}

	protected override get loading() {
		return !this.directives.value || !this.decrees.value
	}

	protected override get layoutKey() {
		return 'lunar-reflection'
	}

	protected override get emptyLabel(): string {
		return 'No decree takes part in Lunar Reflection yet'
	}

	protected override renderRow(row: GridRow, record: DataRecord<GridNode>): HTMLTemplateResult {
		return html`
			<p7t-lunar-reflection-grid-item .row=${row as LunarReflectionRow} ${this.grid.row(record)}></p7t-lunar-reflection-grid-item>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lunar-reflection-grid': LunarReflectionGrid
	}
}
