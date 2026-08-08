import { component, css, html, nothing, property, unsafeCSS } from "@a11d/lit"
import { ExecutiveOrder, SystemBriefing } from "@pleiades/sdk";
import { toRomanNumeral } from "@pleiades/sdk/helpers";
import { CardComponent } from "components/design"
import { 'plaintorch-bgx-png' as bannerBg } from 'assets/design'

@component('p7t-briefing-hero')
export class BriefingHero extends CardComponent {
	override preHeading = 'Pleiades Today';

	@property({ type: Object }) briefing?: SystemBriefing

	static override get styles() {
		return css`
			${super.styles}

			:host {
				background: linear-gradient(
					40deg,
					color-mix(in srgb, var(--interactive-accent) 75%, black) -20%,
					color-mix(in srgb, black 80%, transparent) 80%
				), url('${unsafeCSS(bannerBg)}') no-repeat center/cover, var(--background-primary);
				background-blend-mode: color, luminosity;
			}

			.grid {
				display: grid;
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
				height: 90%;
				width: auto;
				top: 10%;
				inset-inline-end: 15%;
			}

			.x-star {
				fill: var(--background-primary);
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

	/** Executive orders of the current onrush that are in effect today. */
	private get activeExecutiveOrders(): ExecutiveOrder[] {
		const orders = this.briefing?.currentOnrush?.executiveOrders ?? []
		const today = todayKey()
		return orders.filter(order =>
			(!order.effectiveFrom || order.effectiveFrom <= today)
			&& (!order.effectiveUntil || order.effectiveUntil >= today))
	}

	override get template() {
		const chapter = this.briefing?.activeLorePages.find(page => page.level === 'Cha')
		const act = this.briefing?.activeLorePages.find(page => page.level === 'Act')
		const phase = this.briefing?.activeLorePages.find(page => page.level === 'p')
		return html`
			<svg class="mask" data-name="Layer 1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 275.45 277.57">
				<path
					d="M166.68,68.67,202.75,85a2.64,2.64,0,0,1,0,4.81l-36.07,16.29a20.51,20.51,0,0,0-10.26,10.26l-16.29,36.07a2.64,2.64,0,0,1-4.81,0L119,116.32a20.55,20.55,0,0,0-10.26-10.26L72.7,89.77a2.64,2.64,0,0,1,0-4.81l36.07-16.29A20.59,20.59,0,0,0,119,58.41l16.29-36.07a2.64,2.64,0,0,1,4.81,0l16.29,36.07A20.55,20.55,0,0,0,166.68,68.67Z"
					class='x-star' />
				<path d="M134.89,289.56c6-24.79-5.29-50.11-15.69-73.41s-20.29-49.7-11.54-73.67"
					class='x-stem' />
				<path d="M144.22,301a128.48,128.48,0,0,1,9.12-97.77c10.2-19.11,25.29-35.64,32.67-56s3.77-47.53-15.58-57.25"
					class='x-stem' />
				<path d="M141.61,275.11c-3.5-14.18,3.1-29,11.81-40.73s19.68-21.83,27-34.48,10.36-29.34,2.32-41.54"
					class='x-stem' />
				<path d="M138,282c-5.95-13-20.73-19.5-29.08-31.08-9.23-12.81-9.32-30.57-3.61-45.29s16.46-26.91,27.82-37.88"
					class='x-stem' />
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
								<div class='exec-order'>
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

/** Local date as a 'YYYY-MM-DD' key, comparable against serialized DateOnly effective dates. */
function todayKey(): string {
	const now = new Date()
	const pad = (value: number) => value.toString().padStart(2, '0')
	return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-hero': BriefingHero
	}
}