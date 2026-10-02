import { component, css, html, HTMLTemplateResult, nothing } from '@a11d/lit'
import type { Decree, Directive } from '@pleiades/sdk'
import { openEntityEditor, type IconName } from '..'
import { saveEntityField, type EditableField } from '../grid/entityActions'
import type { GridRow } from '../grid/entityTree'
import { GridItemBase } from '../grid/GridItemBase'

/** The Lunar Reflection grid's columns besides the title, by key. */
export enum LunarReflectionGridColumn {
	/** The directive that owns the decree. */
	Directive = 'directive',
	/** The decree's orbit, humanised — or "Chaos" when it has none. */
	Orbit = 'orbit'
}

/** A Lunar Reflection row as the grid hands it over: the decree's row, with the directive that owns it. */
export interface LunarReflectionRow extends GridRow {
	readonly directive: Directive
}

/**
 * One line of the Lunar Reflection grid: a decree taking part in Lunar Reflection.
 *
 * The shared {@link GridItemBase} draws the lead (the reflective glyph and the editable title), the open-note column,
 * the right-click menu and the canonical-entity watch. This variant fills the directive and orbit columns, puts an
 * "Edit decree" button in the last action column, and writes a title edit through the decree's update. A decree with
 * no orbit reflects without one, and reads "Chaos", accented.
 */
@component('p7t-lunar-reflection-grid-item')
export class LunarReflectionGridItem extends GridItemBase {
	static override get styles() {
		return css`
			${super.styles}

			.kind {
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, var(--text-normal));
			}

			.cell[data-column='directive'] {
				color: var(--text-muted);
			}

			.chaos {
				color: var(--p7t-flare-accent, var(--interactive-accent));
				font-weight: 500;
				letter-spacing: .04em;
			}
		`
	}

	protected override get kindIcon(): IconName {
		return 'reflective'
	}

	protected override get kindName() {
		return 'Reflecting decree'
	}

	protected override cellContent(column: string): HTMLTemplateResult | typeof nothing {
		const decree = this.row!.entity as Decree
		switch (column) {
			case LunarReflectionGridColumn.Directive:
				return html`<p7t-directive-item .directive=${(this.row as LunarReflectionRow).directive}></p7t-directive-item>`
			case LunarReflectionGridColumn.Orbit:
				return decree.orbit
					? html`<p7t-schedule-item .orbit=${decree.orbit} .calendar=${decree.calendar}></p7t-schedule-item>`
					: html`<span class='chaos'>Chaos</span>`
			default:
				return nothing
		}
	}

	/** The last action column opens the decree's full banner, where its reflection and orbit are edited. */
	protected override get trailingAction(): HTMLTemplateResult {
		return html`
			<p7t-button ghost icon='lucide:pencil' label='Edit decree' @click=${() => openEntityEditor(this.row!.entity as Decree)}></p7t-button>
		`
	}

	protected override async persistField(keyPath: string): Promise<void> {
		await saveEntityField(this.row!.entity as Decree, keyPath as EditableField)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lunar-reflection-grid-item': LunarReflectionGridItem
	}
}
