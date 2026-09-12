import { Component, component, css, html, nothing, property } from '@a11d/lit'
import { ExecutiveOrder } from '@pleiades/sdk'
import '../PleiadesIcon'

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

/**
 * The executive-order detail drawn in {@link BriefingHero}'s exec-order tooltip — the order id, title, effective
 * window and summary. A self-contained element (its own shadow root and styles) so it renders identically wherever
 * the tooltip system places it, independent of any host's shadow scope. An unset window bound resolves against the
 * current onrush.
 */
@component('p7t-executive-order-details')
export class ExecutiveOrderDetails extends Component {
	@property({ type: Object }) order?: ExecutiveOrder
	/** The current onrush, whose dates resolve an order's unset effective bounds. */
	@property({ type: Object }) onrush?: { startDate?: string; endDate?: string }

	static override get styles() {
		return css`
			:host { display: contents; }

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
		`
	}

	/**
	 * A compact "when it started / when it ends" line for the order's effective window. A timeless order is
	 * Onrush-bound, so each unset bound resolves against the current onrush and the line says so.
	 */
	private get windowLabel(): string {
		const order = this.order
		if (!order) {
			return ''
		}

		const from = order.effectiveFrom ?? this.onrush?.startDate
		const until = order.effectiveUntil ?? this.onrush?.endDate

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

	protected override get template() {
		const order = this.order
		if (!order) {
			return nothing
		}

		return html`
			<div class='eo-tip'>
				<div class='eo-tip-head'>
					<p7t-icon icon='exec-order'></p7t-icon>
					<span class='eo-tip-id'>Executive Order ${order.id}</span>
				</div>
				<div class='eo-tip-title'>${order.title}</div>
				<div class='eo-tip-window'>${this.windowLabel}</div>
				${!order.summary ? nothing : html`<div class='eo-tip-summary'>${order.summary}</div>`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-executive-order-details': ExecutiveOrderDetails
	}
}
