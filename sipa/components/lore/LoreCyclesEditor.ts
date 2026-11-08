import { Component, component, css, html, nothing, property, repeat } from '@a11d/lit'
import type { LorePolarisCycles } from '@pleiades/sdk'
import { core, DerivedRef } from '..'

/**
 * A lore page's Polaris cycles, listed in place beneath its banner: every cycle that falls in the page's active period,
 * oldest first, each opening its note when clicked.
 *
 * The period is the core's call (`core.repos.lorePolaris`): from the page's beginning until its next sibling begins,
 * never past the end of an ancestor's period, and through today while nothing has ended it. A page with no beginning
 * has no period, and this draws nothing; the full banner leaves it out for such a page. Editing the beginning above it
 * is a write, which re-reads the period.
 */
@component('p7t-lore-cycles')
export class LoreCyclesEditor extends Component {
	/** The PUCK of the lore page whose period this lists (`Era1/Cha2`). */
	@property() lorePuck = ''

	private readonly listing = new DerivedRef(this, core.repos.lorePolaris, () => this.lorePuck || undefined)

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .6em;
			}

			.heading {
				display: flex;
				align-items: center;
				flex-wrap: wrap;
				gap: .4em;
				font-weight: 600;
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.count {
				font-weight: 400;
				font-variant-numeric: tabular-nums;
				opacity: .7;
			}

			.period {
				display: flex;
				align-items: center;
				gap: .4ch;
				margin-inline-start: auto;
				font-weight: 300;
				font-size: .9em;
				color: var(--text-muted);
			}

			.rows {
				display: flex;
				flex-direction: column;
			}

			.notice {
				display: flex;
				align-items: center;
				gap: .4ch;
				opacity: .6;
				padding: .3em .1em;
			}
		`
	}

	protected override get template() {
		const listing = this.listing.value
		if (!listing) {
			return !this.listing.loading ? html`` : html`<div class='notice'>Loading…</div>`
		}

		if (!listing.beginning) {
			return html``
		}

		// ISO dates compare as strings; a running period whose beginning is still ahead reaches only today.
		const begun = !listing.through || listing.through >= listing.beginning
		return html`
			<div class='heading'>
				<p7t-icon icon='polaris'></p7t-icon>
				<span>Polaris Cycles</span>
				<span class='count'>${listing.cycles.length}</span>
				<span class='period'>${this.periodTemplate(listing, listing.beginning, begun)}</span>
			</div>
			<div class='rows'>
				${listing.cycles.length > 0 ? nothing : html`
					<div class='notice'>${begun ? 'No Polaris cycle in this period yet.' : 'This page has not begun yet.'}</div>
				`}
				${repeat(listing.cycles, cycle => cycle.id, cycle => html`
					<p7t-polaris-cycle-item interactive .entity=${cycle}></p7t-polaris-cycle-item>
				`)}
			</div>
		`
	}

	/**
	 * The span the list covers: from the beginning to its last day once the period has ended, from the beginning on
	 * while it runs, and the day it will begin while that is still ahead.
	 */
	private periodTemplate(listing: LorePolarisCycles, beginning: string, begun: boolean) {
		if (!begun) {
			return html`<span>Begins</span><p7t-datetime-view .date=${beginning}></p7t-datetime-view>`
		}

		return !listing.endingExclusive || !listing.through
			? html`<span>Since</span><p7t-datetime-view .date=${beginning}></p7t-datetime-view>`
			: html`
				<p7t-datetime-view .date=${beginning}></p7t-datetime-view>
				<span>–</span>
				<p7t-datetime-view .date=${listing.through}></p7t-datetime-view>
			`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lore-cycles': LoreCyclesEditor
	}
}
