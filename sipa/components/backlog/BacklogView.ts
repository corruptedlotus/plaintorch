import { Component, component, css, html, query, state } from '@a11d/lit'
import { core, DerivedRef } from '..'
import { stellarBacklogSource, type GridEntity } from '../grid/entityTree'
import type { SearchCriteria, SearchFilter } from '../search/SearchFilterController'
import { backlogDirectiveFilter, backlogSearchText, backlogStatusFilter, backlogTypeFilter } from './backlogFilters'

/**
 * The briefing's backlog page: the Stellar Backlog — every stellar directive and the incentives they hold, in the
 * backlog grid — under a hero with a search box.
 *
 * The hero is the Moonlight page's, compact and without a banner. The search box sits just beneath its title and sticks
 * to the top of the pane (below the briefing's tab bar) as the grid scrolls; the grid's own heading row sticks below it
 * in turn. The box narrows the grid by title and by `type:`, `status:` and `directive:` filters (see backlogFilters);
 * each result is shown inside its hierarchy, and with no status chosen the archived states stay hidden.
 */
@component('p7t-backlog-view')
export class BacklogView extends Component {
	private readonly directives = new DerivedRef(this, core.repos.directiveList)

	/** What the search box asks of the grid; unset until it first says. */
	@state() private criteria?: SearchCriteria<GridEntity>

	@query('.search') private readonly searchElement?: HTMLElement

	/** Hands the sticky search bar's height to the grid, whose heading row sticks below it. */
	private readonly searchObserver = new ResizeObserver(([entry]) => {
		const height = entry?.borderBoxSize[0]?.blockSize ?? entry?.target.getBoundingClientRect().height ?? 0
		this.style.setProperty('--p7t-backlog-search-height', `${height}px`)
	})

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				font-family: var(--font-interface);
			}

			/*
			 * Sticks beneath the briefing's tab bar, over a backdrop reaching to the page panel's edges so the grid
			 * scrolling beneath does not show through. The backdrop is the panel's own surface (its 20% black over the
			 * pane), so it is unseen until something slides under it; once stuck it also casts a shadow.
			 */
			.search {
				position: sticky;
				top: var(--p7t-page-sticky-top, 0px);
				z-index: 3;
				container-type: scroll-state;
			}

			.backdrop {
				margin-inline: -.5em;
				padding: .7em .5em .8em;
				background-color: color-mix(in srgb, black 20%, var(--background-primary));
				transition: box-shadow .2s ease;
			}

			@container scroll-state(stuck: top) {
				.backdrop {
					box-shadow: 0 .8em 1em -1em rgb(0 0 0 / .6);
				}
			}

			p7t-search-box {
				max-width: 46em;
				margin-inline: auto;
			}

			p7t-entity-grid {
				--p7t-grid-header-top: calc(var(--p7t-page-sticky-top, 0px) + var(--p7t-backlog-search-height, 0px));
			}
		`
	}

	protected override connected() {
		void this.updateComplete.then(() => {
			if (this.isConnected && this.searchElement) {
				this.searchObserver.observe(this.searchElement)
			}
		})
	}

	protected override disconnected() {
		this.searchObserver.disconnect()
	}

	/** The filters the search offers, the directives' drawn afresh from the listing as it changes. */
	private get filters(): readonly SearchFilter<GridEntity, any>[] {
		const directives = stellarBacklogSource({ directives: this.directives.value }).directives ?? []
		return [backlogTypeFilter, backlogStatusFilter, backlogDirectiveFilter(directives)]
	}

	protected override get template() {
		return html`
			<p7t-page-hero compact heading='Stellar Backlog' icon='directive'></p7t-page-hero>
			<div class='search'>
				<div class='backdrop'>
					<p7t-search-box
						placeholder='Search titles and codenames, or filter with type:, status: and directive:'
						.filters=${this.filters}
						.textOf=${backlogSearchText}
						@criteriachange=${(e: CustomEvent<SearchCriteria<GridEntity>>) => this.criteria = e.detail}>
					</p7t-search-box>
				</div>
			</div>
			<p7t-entity-grid .criteria=${this.criteria}></p7t-entity-grid>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-backlog-view': BacklogView
	}
}
