import { component, css, html, HTMLTemplateResult, nothing } from '@a11d/lit'
import { LorePage } from '@pleiades/sdk'
import { core, ExpandingAction, IconName } from '..'
import { GridItemBase } from './GridItemBase'
import { loreLevelIcon, loreRowActions } from './loreActions'

/**
 * One line of the lore grid: an Era, Chapter, Act, or Phase.
 *
 * The shared {@link GridItemBase} draws the indent lanes, the editable name, and the trailing open-note/add columns.
 * This variant fills the two middle cells — the editable beginning date and a read-only "active" marker — draws the
 * level's icon, offers creation of its successive level, and writes name/beginning edits through the lore SDK.
 */
@component('p7t-lore-grid-item')
export class LoreGridItem extends GridItemBase {
	static override get styles() {
		return css`
			${super.styles}

			.lane[data-guide='lore'] {
				border-inline-start-style: solid;
				border-inline-start-color: color-mix(in srgb, var(--text-normal) 30%, transparent);
			}

			/* The active marker (and only it) carries the accent, matching the row's accented wash. */
			.active-marker {
				color: var(--interactive-accent);
				width: 1.1em;
				height: 1.1em;
			}
		`
	}

	protected override get kindIcon(): IconName {
		return loreLevelIcon((this.row!.entity as LorePage).level)
	}

	protected override get middleCells(): (HTMLTemplateResult | typeof nothing)[] {
		return [this.beginningCell, this.activeCell]
	}

	/** The lore period's start date, edited in place and written straight through to frontmatter. */
	protected get beginningCell(): HTMLTemplateResult {
		return html`<p7t-editable-date ${this.binder.bind('beginning')}></p7t-editable-date>`
	}

	/** A read-only marker shown only on a row that is part of the active lore spine. */
	protected get activeCell(): HTMLTemplateResult | typeof nothing {
		return this.row?.active
			? html`<p7t-icon class='active-marker' icon='lucide:circle-dot' aria-label='Active'></p7t-icon>`
			: nothing
	}

	protected override get actions(): ExpandingAction[] {
		return loreRowActions(this.row!.entity as LorePage)
	}

	protected override async persistField(keyPath: string): Promise<void> {
		const page = this.row!.entity as LorePage
		switch (keyPath) {
			case 'title':
				await core.lore.update(page.id, { title: page.title })
				break
			case 'beginning':
				await core.lore.update(page.id, { beginning: page.beginning ?? null })
				break
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lore-grid-item': LoreGridItem
	}
}
