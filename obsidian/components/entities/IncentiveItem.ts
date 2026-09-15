import { component, css, html, property } from '@a11d/lit'
import { Fate, Objective } from '@pleiades/sdk'
import { IconName } from 'components/PleiadesIcon'
import { InfoItem } from '../design/InfoItem'

/** The PEP100 parent family — an objective or a fate — the pair the dependency system calls incentives. */
export type Incentive = Objective | Fate

/**
 * An incentive (PEP100) — an objective or a fate — as a compact chip: its kind glyph beside its title, the same
 * shape the {@link DirectiveItem | directive chip} draws. Objective and fate read apart by their glyph, the same
 * two the grid and banners use. With no incentive it shows a dimmed placeholder.
 */
@component('p7t-incentive-item')
export class IncentiveItem extends InfoItem {
	@property({ type: Object }) incentive?: Incentive
	/** Shown when there is no incentive to name — an unresolved endpoint, say. */
	@property() placeholder = 'Incentive'

	static override get styles() {
		return css`
			${super.styles}

			.placeholder {
				opacity: .5;
				font-weight: 400;
				font-size: .9em;
				line-height: .9;
			}
		`
	}

	/** A fate and an objective are told apart by type, the same split the parent system draws. */
	private get isFate() {
		return this.incentive instanceof Fate
	}

	protected override get bulletIcon(): IconName | undefined {
		return this.incentive ? (this.isFate ? 'fate' : 'objective') : undefined
	}

	protected override get bulletText() {
		return this.incentive?.title ?? this.placeholder
	}

	/** With no incentive the chip is just a dimmed placeholder, outside the icon-text layout. */
	protected override get content() {
		if (!this.incentive) {
			return html`<span class='placeholder'>${this.placeholder}</span>`
		}

		return super.content
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-incentive-item': IncentiveItem
	}
}
