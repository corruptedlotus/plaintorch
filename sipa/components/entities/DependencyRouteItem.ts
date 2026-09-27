import { Component, component, css, html, property } from '@a11d/lit'
import type { EndpointRef } from '@pleiades/sdk'
import { core, DerivedRef } from '../data'
import { tooltip } from '../design/Tooltip'
import { dependencyRoute, type RouteSide } from '../canvas/graphModel'
import './EndpointList'
import '../PleiadesIcon'

/**
 * An entity's place in the dependency graph (PEP101), drawn as a route glyph between two counts: on the left the unmet
 * dependencies blocking it in any way, on the right the unmet dependencies it blocks. Hovering a count lists the
 * entities on that side through the shared {@link EndpointList}, every kind of them, with the ones not in the way of
 * the next lifecycle transition drawn faint.
 *
 * When anything blocks the entity's own next transition — its begin before it has begun, its finish once it has, a
 * checkpoint's unlock — the glyph turns to a broken route and it and the left count take the warning colour. Which
 * gate concerns the next transition is the core's call (`Dependency.gatesNextTransition`); this only reads the shared
 * dependency listing, so it follows every change the listing hears of.
 */
@component('p7t-dependency-route')
export class DependencyRouteItem extends Component {
	/** The entity, addressed the way a dependency endpoint addresses it. Unset draws nothing. */
	@property({ attribute: false }) endpoint?: EndpointRef

	private readonly dependencies = new DerivedRef(this, core.repos.dependencyList)

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
			}

			.route {
				display: inline-flex;
				align-items: center;
				gap: .5ch;
				font-variant-numeric: tabular-nums;
				font-weight: 400;
				color: var(--text-muted);
			}

			/* Nothing either way: the route is there to be read, not to draw the eye. */
			.route.clear {
				opacity: .55;
			}

			.route p7t-icon {
				width: 1.3em;
				height: 1.3em;
			}

			.count {
				min-width: 1ch;
				text-align: center;
				cursor: help;
			}

			.warn {
				color: var(--text-warning);
			}

			.none {
				color: var(--text-muted);
				font-style: italic;
			}
		`
	}

	protected override get template() {
		const endpoint = this.endpoint
		if (!endpoint) {
			return html``
		}

		const route = dependencyRoute(this.dependencies.value ?? [], endpoint)
		const blockedBy = route.blockedBy.dependencies.length
		const blocks = route.blocks.dependencies.length
		const warn = route.nextBlocked
		return html`
			<span class='route ${blockedBy === 0 && blocks === 0 ? 'clear' : ''}'>
				<span class='count ${warn ? 'warn' : ''}' ${tooltip(() => this.sideTemplate(route.blockedBy, 'Blocked by', 'Nothing blocks this'))}>${blockedBy}</span>
				<p7t-icon class=${warn ? 'warn' : ''} icon=${warn ? 'lucide:route-off' : 'lucide:route'}></p7t-icon>
				<span class='count' ${tooltip(() => this.sideTemplate(route.blocks, 'Blocking', 'Blocks nothing'))}>${blocks}</span>
			</span>
		`
	}

	/** One side's entities, headed, with the ones not in the way of a next transition drawn faint; a note when empty. */
	private sideTemplate(side: RouteSide, heading: string, empty: string) {
		return side.endpoints.length === 0
			? html`<span class='none'>${empty}</span>`
			: html`<p7t-endpoint-list .endpoints=${side.endpoints} .nonImmediate=${side.nonImmediate}>${heading}</p7t-endpoint-list>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-dependency-route': DependencyRouteItem
	}
}
