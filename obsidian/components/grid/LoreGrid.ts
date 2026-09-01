import { component, html, HTMLTemplateResult } from '@a11d/lit'
import { core, DerivedRef, ExpandingAction } from '..'
import { buildLoreRows } from './loreTree'
import { GridBase } from './GridBase'
import { loreCreationActions } from './loreActions'
import type { GridRow } from './entityTree'
import './LoreGridItem'

/**
 * The lore grid: the whole Era → Chapter → Act → Phase hierarchy as one nested run of rows.
 *
 * The shell — column tracks, expansion, the sticky FAB — is the shared {@link GridBase}, exactly as the backlog. This
 * variant sources the lore listing, builds the hierarchy tree, draws each row with the lore row element, and offers a
 * single FAB action: a new top-level Era (each row offers its own successive level).
 */
@component('p7t-lore-grid')
export class LoreGrid extends GridBase {
	private readonly lore = new DerivedRef(this, core.repos.loreList)

	protected override get rows(): GridRow[] {
		// Expanded by default so the whole hierarchy shows at once; the toggle set tracks user-collapsed rows.
		return buildLoreRows(this.lore.value ?? [], this.expandedKeys, true)
	}

	protected override get loading() {
		return !this.lore.value
	}

	protected override get emptyLabel(): string {
		return 'No lore yet'
	}

	protected override get fabLabel(): string {
		return 'Add an Era'
	}

	protected override get fabActions(): ExpandingAction[] {
		return loreCreationActions()
	}

	protected override renderRow(row: GridRow): HTMLTemplateResult {
		return html`<p7t-lore-grid-item .row=${row}></p7t-lore-grid-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lore-grid': LoreGrid
	}
}
