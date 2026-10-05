import { Component, component, css, html } from '@a11d/lit'
import { core, DerivedRef } from '..'
import './ActiveLore'

/**
 * The briefing's Lore page: the lore grid under a hero, as the Stellar Backlog's page is — the shared compact hero with
 * its title and glyph — but with the active lore where the backlog has its search box: the chapter, act and phase the
 * briefing's hero shows, a little smaller and centred under the title.
 *
 * The active lore is the briefing's own (the core's one active-lore definition), so the page and the hero always agree.
 */
@component('p7t-lore-view')
export class LoreView extends Component {
	private readonly briefing = new DerivedRef(this, core.repos.briefing)

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				font-family: var(--font-interface);
			}

			/* Where the backlog's search bar stands, at its padding, not sticking: the lore is read, not worked with. */
			.active {
				padding: .4em .5em 1em;
			}

			p7t-active-lore {
				font-size: .9em;
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-page-hero compact heading='Lore & Saga' icon='lorepage'></p7t-page-hero>
			<div class='active'>
				<p7t-active-lore centred emptyLabel='No lore is active yet' .pages=${this.briefing.value?.activeLorePages}></p7t-active-lore>
			</div>
			<p7t-lore-grid></p7t-lore-grid>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lore-view': LoreView
	}
}
