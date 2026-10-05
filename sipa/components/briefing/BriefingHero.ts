import { component, css, html, nothing, property } from "@a11d/lit"
import { ExecutiveOrder, SystemBriefing } from "@pleiades/sdk";
import { CardComponent, tooltip } from "../design"
import '../lore/ActiveLore'
import './ExecutiveOrderDetails'

/**
 * The briefing's hero: today's date under "Pleiades Today", the active lore beside its glyph, the banked starfire and the
 * executive orders in effect, over the star.
 *
 * {@link ducked}, it keeps only the date and the starfire, on one short strip: the rest folds away (its space closing
 * as it fades) and the hero shrinks with it. The briefing ducks it on every page but the throne room. The fold takes
 * `--p7t-hero-duck-duration` (320ms by default; 0 to fold at once).
 */
@component('p7t-briefing-hero')
export class BriefingHero extends CardComponent {
	override preHeading = 'Pleiades Today';

	@property({ type: Object }) briefing?: SystemBriefing
	/** Keeps only the date and the starfire, folding the rest away. */
	@property({ type: Boolean, reflect: true }) ducked = false

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--_duck: var(--p7t-hero-duck-duration, 320ms) cubic-bezier(.4, 0, .2, 1);
				padding: 0;
				background: none;
				background-blend-mode: color, luminosity;
				interpolate-size: allow-keywords;
			}

			.grid {
				display: grid;
				z-index: 1;
				grid-template-rows: auto auto;
				grid-template-columns: 1fr auto;
				grid-template-areas:
					"header extra"
					"lore extra";
				gap: 1rem;
			}

			.lore {
				grid-area: lore;
			}

			.extra {
				display: flex;
				flex-direction: column;
				align-items: flex-end;
				justify-content: space-between;
				gap: .8rem;
				grid-area: extra;
			}

			.info-chip {
				display: flex;
				align-items: center;
				grid-area: extra;
				gap: 4px;
				font-size: .9em;
				font-weight: 400;
				font-family: var(--font-text);
				background-color: color-mix(in srgb, var(--text-accent) 6%, transparent);
				border-radius: 8px;
				padding-inline: .6em .4em;
				padding-block: .2em;

				& p7t-icon {
					width: 24px;
					height: 24px;
					opacity: .91;
				}
			}

			/* Active executive orders sit at the bottom-right, sharing the Celestron chip's
			   look but far less opaque. Colour is a temporary yellow. */
			.exec-orders {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				gap: 3px;
				background-color: color-mix(in srgb, var(--text-normal) 8%, transparent);
				border-radius: 8px;
				padding: .35em .6em;
				color: #ffd23f;
				max-width: 24em;
				position: relative;
				z-index: 2;
				font-weight: 500;
			}

			.exec-order {
				display: flex;
				align-items: center;
				gap: 6px;
				font-size: .82em;
				font-weight: 400;
				font-family: var(--font-text);
				line-height: .9;
				cursor: help;

				& p7t-icon {
					width: 18px;
					height: 18px;
					flex: 0 0 18px;
					margin-top: .1em;
				}
			}

			.mask {
				position: absolute;
				z-index: 0;
				height: 120%;
				width: 100%;
				top: -10%;
			}

			.x-star {
				fill: var(--background-primary-alt);
			}

			.x-stem {
				fill: none;
				stroke: var(--background-primary);
				stroke-linecap: round;
				stroke-miterlimit: 10;
				stroke-width: 10px;
			}

			:host::part(header) {
				grid-area: header;
			}

			/*
			 * The duck. Each part that goes folds its own space away as it fades (its height to nothing, and the gap it held
			 * open with it), so the hero shrinks smoothly around what stays: the date at the start of the strip, the starfire
			 * at its end.
			 */
			.pre-heading, .lore, .exec-orders {
				transition:
					height var(--_duck),
					margin var(--_duck),
					opacity var(--_duck),
					visibility var(--_duck);
			}

			.pre-heading {
				display: block;
			}

			.mask {
				transition: opacity var(--_duck);
			}

			.grid {
				transition: row-gap var(--_duck);
			}

			:host([ducked]) {
				.pre-heading, .lore, .exec-orders {
					height: 0;
					overflow: clip;
					opacity: 0;
					visibility: hidden;
				}

				/*
				 * The gaps the folded parts held open: the header's above the date and the extra column's above the orders,
				 * taken back by a negative margin; the grid's above the lore, closed outright (a grid track does not shrink
				 * below nothing for an item's negative margin).
				 */
				.pre-heading { margin-bottom: -.4em; }
				.exec-orders { margin-top: -.8rem; }

				.grid {
					row-gap: 0;
					align-items: center;
				}

				.mask {
					opacity: 0;
				}
			}

			@media (prefers-reduced-motion: reduce) {
				.pre-heading, .lore, .exec-orders {
					transition-property: opacity, visibility;
				}
			}
		`
	}

	protected override get preHeadingTemplate() {
		// Its own box rather than the bare text, so the duck can fold it.
		return html`<span class='pre-heading'>${this.preHeading}</span>`
	}

	/**
	 * Executive orders of the current onrush that are in effect today. Effectiveness is resolved by the core
	 * (a timeless order is Onrush-bound), so this trusts the served `isActive` flag rather than recomputing.
	 */
	private get activeExecutiveOrders(): ExecutiveOrder[] {
		return (this.briefing?.currentOnrush?.executiveOrders ?? []).filter(order => order.isActive)
	}

	override get template() {
		return html`
			<svg class="mask" data-name="Layer 1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 275.45 200.57">
				<path
					d="M166.68,68.67,202.75,85a2.64,2.64,0,0,1,0,4.81l-36.07,16.29a20.51,20.51,0,0,0-10.26,10.26l-16.29,36.07a2.64,2.64,0,0,1-4.81,0L119,116.32a20.55,20.55,0,0,0-10.26-10.26L72.7,89.77a2.64,2.64,0,0,1,0-4.81l36.07-16.29A20.59,20.59,0,0,0,119,58.41l16.29-36.07a2.64,2.64,0,0,1,4.81,0l16.29,36.07A20.55,20.55,0,0,0,166.68,68.67Z"
					class='x-star' />
				<path
					d="M145.94,82.05,156,86.61a.83.83,0,0,1,0,1.51l-10.09,4.55A5.87,5.87,0,0,0,143,95.59l-4.55,10.08a.83.83,0,0,1-1.51,0l-4.56-10.08a5.83,5.83,0,0,0-2.91-2.92l-10.09-4.55a.83.83,0,0,1,0-1.51l10.09-4.56a5.77,5.77,0,0,0,2.91-2.91L137,69.05a.83.83,0,0,1,1.51,0L143,79.14A5.81,5.81,0,0,0,145.94,82.05Z"
					style="fill:#21d3ca" />
				<path
					d="M145.94,82.05,156,86.61a.83.83,0,0,1,0,1.51l-10.09,4.55A5.87,5.87,0,0,0,143,95.59l-4.55,10.08a.83.83,0,0,1-1.51,0l-4.56-10.08a5.83,5.83,0,0,0-2.91-2.92l-10.09-4.55a.83.83,0,0,1,0-1.51l10.09-4.56a5.77,5.77,0,0,0,2.91-2.91L137,69.05a.83.83,0,0,1,1.51,0L143,79.14A5.81,5.81,0,0,0,145.94,82.05Z"
					style="fill:#82fff9; filter: blur(20px)" />
			</svg>
			<div class='grid'>
				${this.headerTemplate}
				<p7t-active-lore class='lore' glyph .pages=${this.briefing?.activeLorePages}></p7t-active-lore>
				<div class='extra'>
					<div class='info-chip'>
						<span>${this.briefing?.celestronBanked ?? 0}</span>
						<p7t-icon icon='starfire'></p7t-icon>
					</div>
					${this.activeExecutiveOrders.length === 0 ? nothing : html`
						<div class='exec-orders'>
							Executive Orders in Effect
							${this.activeExecutiveOrders.map(order => html`
								<div class='exec-order' ${tooltip(() => html`<p7t-executive-order-details .order=${order} .onrush=${this.briefing?.currentOnrush}></p7t-executive-order-details>`)}>
									<p7t-icon icon='exec-order'></p7t-icon>
									<span>${order.id}: ${order.title}</span>
								</div>
							`)}
						</div>
					`}
				</div>
			</div>
		`
	}

	override get headingTemplate() {
		return html`
			<p7t-date-view></p7t-date-view>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-hero': BriefingHero
	}
}