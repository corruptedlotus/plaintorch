import { component, css, html, nothing, property, unsafeCSS } from "@a11d/lit"
import { ExecutiveOrder, SystemBriefing } from "@pleiades/sdk";
import { toRomanNumeral } from "@pleiades/sdk/helpers";
import { CardComponent, tooltip } from "components/design"
import { 'plaintorch-bgx-png' as bannerBg } from 'assets/design'

@component('p7t-briefing-hero')
export class BriefingHero extends CardComponent {
	override preHeading = 'Pleiades Today';

	@property({ type: Object }) briefing?: SystemBriefing

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding: 0;
				background: none;
				background-blend-mode: color, luminosity;
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
				display: flex;
				align-items: center;
				gap: 10px;
				align-items: center;
				grid-area: lore;

				& p7t-icon {
					width: 64px;
					height: 64px;
				}

				& .info {
					display: flex;
					flex-direction: column;
				}

				& .chapter {
					font-size: 1em;
					opacity: .6;
					font-weight: 500;
					margin-bottom: -.2em;
				}

				& .act {
					font-size: 1.4em;
					font-weight: 300;

					& :first-child {
						opacity: .6;
						margin-inline-end: .1ch;
					}
				}

				& .phase {
					margin-top: .05em;
					font-family: 'Georgia', serif;
					font-size: .9em;
					color: color-mix(in srgb, var(--text-normal) 80%, transparent);
					font-style: italic;
					font-weight: 300;

					& :first-child {
						font-family: var(--font-text);
						background-color: color-mix(in srgb, var(--text-normal) 15%, transparent);
						color: var(--text-normal);
						padding: 0 .8ch;
						font-style: normal;
						font-weight: 800;
						margin-inline-end: .8ch;
						border-radius: .6em;
					}
				}
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
				background-color: color-mix(in srgb, var(--text-normal) 15%, transparent);
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

			/* Rendered into the tooltip overlay by the directive, but styled here where the markup lives. */
			.eo-tip {
				display: flex;
				flex-direction: column;
				gap: .25em;
			}

			.eo-tip-head {
				display: flex;
				align-items: center;
				gap: .45ch;
				color: #ffd23f;

				& p7t-icon {
					width: 18px;
					height: 18px;
				}
			}

			.eo-tip-id {
				font-size: .78em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .04em;
			}

			.eo-tip-title {
				font-size: 1.05em;
				font-weight: 500;
				line-height: 1.15;
			}

			.eo-tip-window {
				font-size: .8em;
				font-weight: 500;
				opacity: .7;
			}

			.eo-tip-summary {
				margin-top: .15em;
				font-size: .9em;
				font-weight: 400;
				line-height: 1.35;
				opacity: .85;
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
		`
	}

	/**
	 * Executive orders of the current onrush that are in effect today. Effectiveness is resolved by the core
	 * (a timeless order is Onrush-bound), so this trusts the served `isActive` flag rather than recomputing.
	 */
	private get activeExecutiveOrders(): ExecutiveOrder[] {
		return (this.briefing?.currentOnrush?.executiveOrders ?? []).filter(order => order.isActive)
	}

	/**
	 * A compact "when it started / when it ends" line for an order's effective window. A timeless order is
	 * Onrush-bound, so each unset bound resolves against the current onrush and the line says so.
	 */
	private effectiveWindowLabel(order: ExecutiveOrder): string {
		const onrush = this.briefing?.currentOnrush
		const from = order.effectiveFrom ?? onrush?.startDate
		const until = order.effectiveUntil ?? onrush?.endDate

		const parts: string[] = []
		if (order.isOnrushBound) {
			parts.push('Onrush-bound')
		}
		else if (from) {
			parts.push(`Since ${formatShortDate(from)}`)
		}

		if (until) {
			const days = daysFromToday(until)
			parts.push(days < 0
				? `Ended ${formatShortDate(until)}`
				: days === 0 ? 'Ends today' : `Ends in ${days}d`)
		}

		return parts.join(' · ')
	}

	override get template() {
		const chapter = this.briefing?.activeLorePages.find(page => page.level === 'Cha')
		const act = this.briefing?.activeLorePages.find(page => page.level === 'Act')
		const phase = this.briefing?.activeLorePages.find(page => page.level === 'p')
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
				<div class='lore'>
					<p7t-icon icon="lorepage"></p7t-icon>
					<div class='info'>
						${!chapter ? nothing : html`
							<span class='chapter'>
								${chapter?.title}
							</span>
						`}
						${!act ? nothing : html`
							<span class='act'>
								<span>${act?.overrideIdentifier ?? `Act ${toRomanNumeral(act?.act ?? 0)}`}:</span>
								${act?.title}
							</span>
						`}
						${!phase ? nothing : html`
							<span class='phase'>
								<span>${phase?.overrideIdentifier ?? phase?.phase}</span>
								"${phase?.title}"
							</span>
						`}
					</div>
				</div>
				<div class='extra'>
					<div class='info-chip'>
						<span>${this.briefing?.celestronBanked ?? 0}</span>
						<p7t-icon icon='starfire'></p7t-icon>
					</div>
					${this.activeExecutiveOrders.length === 0 ? nothing : html`
						<div class='exec-orders'>
							Executive Orders in Effect
							${this.activeExecutiveOrders.map(order => html`
								<div class='exec-order' ${tooltip(() => html`
									<div class='eo-tip'>
										<div class='eo-tip-head'>
											<p7t-icon icon='exec-order'></p7t-icon>
											<span class='eo-tip-id'>Executive Order ${order.id}</span>
										</div>
										<div class='eo-tip-title'>${order.title}</div>
										<div class='eo-tip-window'>${this.effectiveWindowLabel(order)}</div>
										${!order.summary ? nothing : html`<div class='eo-tip-summary'>${order.summary}</div>`}
									</div>
								`)}>
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

/** 'YYYY-MM-DD' → a short 'Aug 12' label, parsed as local time so the day never shifts across a timezone. */
function formatShortDate(date: string): string {
	const parsed = new Date(`${date}T00:00:00`)
	return Number.isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

/** Whole days from local midnight today to the given 'YYYY-MM-DD' date (negative when past). */
function daysFromToday(date: string): number {
	const target = new Date(`${date}T00:00:00`)
	if (Number.isNaN(target.getTime())) return 0
	const now = new Date()
	const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
	return Math.round((target.getTime() - startOfToday.getTime()) / 86_400_000)
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-hero': BriefingHero
	}
}