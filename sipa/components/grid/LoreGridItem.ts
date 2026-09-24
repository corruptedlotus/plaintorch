import { component, css, html, HTMLTemplateResult, nothing } from '@a11d/lit'
import { LorePage } from '@pleiades/sdk'
import { core, ExpandingAction, IconName } from '..'
import { openNotePath } from './entityActions'
import { GridItemBase } from './GridItemBase'
import { loreLevelIcon, loreLevelLabel, loreOwnIndex, loreRowActions } from './loreActions'
import { toast } from '../../host'

/**
 * One line of the lore grid: an Era, Chapter, Act, or Phase.
 *
 * The shared {@link GridItemBase} draws the indent lanes, the editable name, and the trailing open-note/add columns.
 * This variant fills the leading label (the level and its editable index), the two middle cells (the editable
 * beginning date and a read-only "active" marker), draws the level's icon, offers creation of its successive level,
 * and writes name/beginning edits through the lore SDK.
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

			.leading {
				display: flex;
				align-items: center;
				margin-inline: .4em;
				gap: .35em;
			}

			.leading .level {
				opacity: .55;
				text-transform: uppercase;
				letter-spacing: .05em;
				font-size: .8em;
			}

			.leading .index {
				font-weight: 500;
				min-width: 1ch;
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

	/** The level name and its editable index — "Era 1". Editing the index renumbers the page (and its subtree). */
	protected override get leadingCell(): HTMLTemplateResult {
		const page = this.row!.entity as LorePage
		const index = loreOwnIndex(page)
		return html`
			<span class='level'>${loreLevelLabel(page.level)}</span>
			<p7t-editable-plaintext
				class='index'
				required
				label='Index'
				.value=${index != null ? String(index) : ''}
				@edit=${(e: CustomEvent<string | undefined>) => this.commitIndex(e.detail)}>
			</p7t-editable-plaintext>
		`
	}

	/**
	 * Renumbers the page to a typed index. This re-keys the page and its whole subtree server-side, so the listing is
	 * re-read on success; an invalid or unchanged entry simply snaps the field back to the stored index.
	 */
	protected async commitIndex(raw: string | undefined) {
		const page = this.row!.entity as LorePage
		const value = Number.parseInt((raw ?? '').trim(), 10)
		// The core accepts index 0 (a prologue — Chapter 0 / Act 0); only negatives are rejected. Guarding `< 1` here
		// silently dropped a "0" as if it were unset, so the renumber never reached the API.
		if (!Number.isInteger(value) || value < 0 || value === loreOwnIndex(page)) {
			this.requestUpdate()
			return
		}

		const updated = await core.lore.setIndex(page.id, value)
		if (!updated) {
			toast('Could not renumber that lore page — that index may already be taken.', 'error')
			this.requestUpdate()
			return
		}

		toast(`Renumbered to ${loreLevelLabel(updated.level)} ${value}.`, 'success')
		await core.repos.loreList.refresh()
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
			? html`<p7t-icon class='active-marker' icon='lucide:astroid' aria-label='Active'></p7t-icon>`
			: nothing
	}

	protected override get actions(): ExpandingAction[] {
		return loreRowActions(this.row!.entity as LorePage)
	}

	// Lore pages carry their own note path, so open it directly rather than resolving it by PUCK — lore ids are not in
	// the PUCK registry, so the generic resolution can miss them even though the note plainly exists.
	protected override async open() {
		const page = this.row!.entity as LorePage
		if (!page.relativePath) {
			toast('That lore page has no note yet.', 'warning')
			return
		}

		openNotePath(page.relativePath)
	}

	protected override async persistField(keyPath: string): Promise<void> {
		const page = this.row!.entity as LorePage
		switch (keyPath) {
			case 'title':
				await core.lore.update(page.id, { title: page.title })
				break
			case 'beginning':
				await core.lore.update(page.id, { beginning: page.beginning ?? null })
				// The beginning date is what decides the active spine, so an edit can move "active" to a different page;
				// re-read the listing so every row's server-stamped `isActive` reflects the new spine, not just this one.
				await core.repos.loreList.refresh()
				break
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lore-grid-item': LoreGridItem
	}
}
