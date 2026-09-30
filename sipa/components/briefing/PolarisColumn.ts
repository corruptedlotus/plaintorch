import { component, css, html, nothing, property, state, type PropertyValues } from "@a11d/lit"
import { isoDay, isoDaysBetween } from "@pleiades/sdk"
import { PolarisBriefingBase } from "./PolarisBriefingBase"
import {
	canMovePolarisActivity, core, movePolarisActivity, polarisActivityKinds, revalidatePolarisCycles, TransferController,
	type PolarisActivity
} from ".."
import { toast } from "../../host"

/**
 * One day of the forecast: that day's Polaris cycle as a narrow column, the simpler sibling of the briefing's card
 * (both are {@link PolarisBriefingBase}). It plans a day rather than works it — no timer, no reflectives, no progress:
 *
 * - the executives are in planning mode, each notch the executive's estimate, edited in place;
 * - the foot of the list adds an activity to THIS day's cycle;
 * - an executive dragged onto another day's column moves there, refused where that day already holds the same
 *   objective or decree; dragged onto its own column's footer (the bin) it is removed;
 * - the footer totals the day's estimates, and offers Delete — for a plan only. The active cycle shows nothing there,
 *   and an ended day is a record: it takes no changes at all.
 *
 * With no cycle the column is the day's empty slot: a button planning the day when it is {@link plannable}, nothing
 * otherwise.
 *
 * The column collapses to its header — its state and total — docked at the foot of its day, leaving the day above it
 * to the calendar. Its host (the forecast) owns the collapsed state, so every day collapses as one row. Wherever the
 * column leaves its day uncovered — collapsed, or with no cycle — it takes no pointer, so the day box beneath stays
 * reachable, whole.
 */
@component('p7t-polaris-column')
export class PolarisColumn extends PolarisBriefingBase {
	/** The day the column stands for, as an ISO date (`2026-09-30`). */
	@property() day?: string

	override collapsible = true

	/** Whether the day, while it has no cycle, is offered for planning: the column then shows the button planning it. */
	@property({ type: Boolean }) plannable = false

	/** While the day's forecast is being planned or deleted. */
	@state() private busy = false

	/**
	 * Takes an executive dragged out of ANOTHER day's column and moves it into this day's cycle. Candidacy is decided at
	 * drag start: this day takes changes, and it holds no executive for the same objective or decree yet.
	 */
	protected readonly moves = new TransferController<PolarisActivity>(this, {
		accepts: polarisActivityKinds,
		canAccept: (activity, transaction) => transaction.source !== this.activities
			&& this.editable
			&& canMovePolarisActivity(activity, this.data!),
		accept: activity => movePolarisActivity(activity, this.data!),
	})

	/** Begun, and not ended: the day being worked. */
	private get active() {
		return !!this.data?.startTime && !this.data.endTime
	}

	/** Begun and ended: a worked day's record. */
	private get ended() {
		return !!this.data?.endTime
	}

	protected override get editable() {
		return !!this.data && !this.ended
	}

	/** A day being planned or worked edits its estimates in place; an ended day shows what its allocations came to. */
	protected override get planning() {
		return !this.ended
	}

	static override get styles() {
		return css`
			${super.styles}

			/* Stretched over its day's cell by the day view, so the list scrolls within the column's height. */
			:host {
				min-height: 0;
				margin: .25rem;
				padding: .6rem .5rem;
				border-radius: 12px;
				gap: .3rem;
				font-size: .9em;
			}

			:host::part(header) {
				padding-inline: .4em;
				margin-block: 0;
			}

			:host::part(pre-heading) {
				font-size: .95em;
			}

			:host::part(heading),
			:host::part(sub-heading) {
				display: none;
			}

			:host::part(content) {
				/* The card's bleed into its padding assumes the card's wider padding. */
				inset-inline: -.5em;
				width: calc(100% + .5em);
				/* A narrow day scrolls its list down, never sideways — the strip around it is what scrolls sideways. */
				overflow-x: hidden;
			}

			:host::part(footer) {
				align-items: center;
			}

			:host([ended]) {
				opacity: .75;
			}

			/* Collapsed with the rest of the row: the header alone, docked at the foot of the day. */
			:host([collapsed]) {
				align-self: end;
				gap: 0;
				padding-block: .35rem;
			}

			/* The day without a cycle: no card at all, only what it offers — and nothing in the way of the day. */
			:host([vacant]) {
				background: none;
				padding: 0;
				outline: none;
				justify-content: center;
				align-items: center;
				pointer-events: none;
			}

			.plan {
				font-size: 1.05em;
				pointer-events: auto;

				:host([collapsed]) & {
					font-size: .95em;
				}
			}

			/* The header line: the state, and — collapsed, the rest being out of sight — the day's total. */
			.pre-heading-line {
				display: inline-flex;
				align-items: center;
				gap: .8ch;

				& p7t-time-unit {
					font-weight: 300;
					color: color-mix(in srgb, currentColor 80%, transparent);
				}
			}

			.total {
				display: flex;
				align-items: center;
				gap: .5ch;
				font-family: var(--font-text);

				& p7t-icon {
					width: 1.3em;
					height: 1.3em;
					opacity: .8;
				}

				& .caption {
					font-size: .8em;
					font-weight: 600;
					text-transform: uppercase;
					letter-spacing: .04em;
					color: var(--text-muted);
				}

				& p7t-time-unit {
					font-size: 1.3em;
					font-weight: 300;
				}
			}

			.add-button {
				margin: .4em .6em;
			}
		`
	}

	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		this.preHeading = this.active ? 'Active' : this.ended ? 'Concluded' : 'Forecast'
		// A collapsing column takes its open creation row with it: the row floats over the list it would stand in.
		if (changed.has('collapsed') && this.collapsed) {
			this.creating = false
		}
	}

	protected override get preHeadingTemplate() {
		return html`
			<span class='pre-heading-line'>
				${super.preHeadingTemplate}
				${!this.collapsed ? nothing : html`<p7t-time-unit .value=${this.totalEstimation}></p7t-time-unit>`}
			</span>
		`
	}

	protected override updated(changed: PropertyValues<this>) {
		super.updated(changed)
		this.toggleAttribute('vacant', !this.data)
		this.toggleAttribute('ended', this.ended)
	}

	protected override get template() {
		return this.data ? super.template : this.offlineTemplate
	}

	protected override get offlineTemplate() {
		return html`
			${!this.plannable ? nothing : html`
				<p7t-button class='plan' icon='polaris' ?disabled=${this.busy} @click=${() => this.plan()}>Plan the Day</p7t-button>
			`}
		`
	}

	protected override get listContent() {
		return html`
			${this.executivesTemplate}
			${this.creationTemplate}
		`
	}

	protected override get footerTemplate() {
		// A plan can be taken back; the active cycle is being worked (nothing to offer here), an ended one is a record.
		const deletable = !!this.data && !this.data.startTime
		return html`
			<span class='total'>
				<p7t-icon icon='state-polaris'></p7t-icon>
				<span class='caption'>Total</span>
				<p7t-time-unit .value=${this.totalEstimation}></p7t-time-unit>
			</span>
			${!deletable ? nothing : html`
				<p7t-button ghost danger icon='lucide:trash-2' ?disabled=${this.busy} @click=${() => this.delete()}>Delete</p7t-button>
			`}
		`
	}

	/** Plans the day: a forecast cycle for it, planned from today. */
	private async plan() {
		const day = this.day
		if (!day || this.data || this.busy) return
		const today = isoDay(new Date())
		this.busy = true
		try {
			const planned = await core.polaris.plan({ forecastReference: today, daysAhead: isoDaysBetween(today, day) })
			if (!planned) {
				toast('PLAINTORCH could not plan the day.', 'error')
				return
			}

			this.data = planned
			await revalidatePolarisCycles()
		}
		finally {
			this.busy = false
		}
	}

	/** Deletes the day's plan — the forecast cycle and the executives in it; the objectives and decrees behind them stay. */
	private async delete() {
		const cycle = this.data
		if (!cycle || cycle.startTime || this.busy) return
		this.busy = true
		try {
			if (!await core.polaris.delete(cycle.id)) {
				toast(`PLAINTORCH could not delete ${cycle.title}.`, 'error')
				return
			}

			this.data = undefined
			// Let the cycle's watch let go of it first, or the revalidation would ask the core for the deleted cycle.
			await this.updateComplete
			await revalidatePolarisCycles()
			toast(`Deleted ${cycle.title}.`, 'success')
		}
		finally {
			this.busy = false
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-polaris-column': PolarisColumn
	}
}
