import { component, html, HTMLTemplateResult } from '@a11d/lit'
import type { DataRecord } from '@3mo/data-grid/controller'
import type { EntityTypeName } from '@pleiades/sdk'
import { core, DerivedRef, ExpandingAction } from '..'
import { buildLoreTree } from './loreTree'
import { GridBase } from './GridBase'
import { loreCreationActions } from './loreActions'
import type { GridNode, GridRow } from './entityTree'
import { LoreGridColumn } from './LoreGridItem'

/**
 * The lore grid: the whole Era → Chapter → Act → Phase hierarchy as one nested run of rows.
 *
 * The shell — the columns and their heading row, expansion, the sticky FAB — is the shared {@link GridBase}, exactly as
 * the backlog. This variant sources the lore listing, builds the hierarchy tree, names its columns, draws each row with
 * the lore row element, and offers a single FAB action: a new top-level Era (each row offers its own successive level).
 */
@component('p7t-lore-grid')
export class LoreGrid extends GridBase {
	private readonly lore = new DerivedRef(this, core.repos.loreList)

	protected override readonly columns = [
		GridBase.titleColumnDefinition(),
		GridBase.column(LoreGridColumn.Beginning, 'Beginning'),
		GridBase.column(LoreGridColumn.Active, 'Active', { alignment: 'center' })
	]

	protected override get observedKinds(): readonly EntityTypeName[] {
		// The hierarchy is built from lore pages' own parent references, so a reparent restructures the tree
		// without the listing changing. Both concrete kinds are named, as the core may stamp either.
		return ['LorePage', 'IndexedLorePage']
	}

	protected override buildTree(): GridNode[] {
		return buildLoreTree(this.lore.value ?? [])
	}

	/** Lore is a small narrative hierarchy meant to be read as a whole, so its rows start open. */
	protected override get expandsByDefault() {
		return true
	}

	protected override get loading() {
		return !this.lore.value
	}

	protected override get layoutKey() {
		return 'lore'
	}

	protected override get emptyLabel(): string {
		return 'No lore yet'
	}

	protected override get fabLabel(): string {
		return 'New Era'
	}

	protected override get fabActions(): ExpandingAction[] {
		return loreCreationActions()
	}

	protected override renderRow(row: GridRow, record: DataRecord<GridNode>): HTMLTemplateResult {
		return html`<p7t-lore-grid-item .row=${row} ${this.grid.row(record)}></p7t-lore-grid-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lore-grid': LoreGrid
	}
}
