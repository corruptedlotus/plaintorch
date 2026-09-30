import { Component, component, css, html, state } from '@a11d/lit'
import { dateOfIsoDay, forecastHorizon, isoDay, type PolarisCycle } from '@pleiades/sdk'
import { core, DerivedRef, type PolarisColumn } from '..'
import { loadMitraEmbed, type MitraRange } from './mitraEmbed'

/**
 * The Forecast tab: the days ahead as a strip of Polaris columns, one per day, for planning each day's cycle before
 * it is begun. The strip is Mitra's embeddable day view (`<mitra-embed>`, see `vendor/mitra`): three days fit the
 * width, starting today, and it scrolls sideways through the rest. PLAINTORCH hands it no calendar entries — it fills
 * every day with that day's `p7t-polaris-column` instead (the view's height-filling day slot).
 *
 * Which days offer planning follows {@link forecastHorizon}: the first three always — today among them — and, once
 * the last of those is planned, one more day past the last planned day, every unplanned day before it included. A day
 * with a cycle shows it wherever it lies, a past one as a record.
 *
 * The cycles come from the `polarisDays` record, read from the first day the view covers; as the strip scrolls, the
 * view names the new span (`range-change`) and the record follows it.
 *
 * The row of columns collapses as one (any column's chevron), down to their headers at the foot of the days, which
 * leaves the days themselves — Mitra's day boxes, where the calendar's own planning and events go — free to use.
 */
@component('p7t-forecast-view')
export class ForecastView extends Component {
	/** How many days fit the width — and how many, from today, always offer planning. */
	static readonly visibleDays = 3

	/** Today, as the ISO date the cycles' days are compared with. Fixed for the view's life, like where it starts. */
	private readonly today = isoDay(new Date())

	/**
	 * Where the strip starts — today's local midnight. One instance for the view's life: the embed takes a new date as
	 * a navigation, so handing it a fresh one on a re-render would scroll a strip the reader has moved back to today.
	 */
	private readonly start = dateOfIsoDay(this.today)

	@state() private ready = false

	/**
	 * Whether the row of Polaris columns is collapsed: every day's column at once, down to its header at the foot of
	 * the day, so the days above are free for the calendar. Any column's chevron toggles the whole row.
	 */
	@state() private collapsed = false

	/** The first day the strip covers (ISO), from its `range-change`; nothing is read before it names one. */
	@state() private from?: string

	private readonly cyclesRef = new DerivedRef(this, core.repos.polarisDays, () => this.from)

	/** The last cycles read. A new span takes a moment to resolve, and the days on screen keep what they show meanwhile. */
	private shownCycles: readonly PolarisCycle[] = []

	/** The column of every day the strip has asked for, so a change of data reaches them in place. */
	private readonly columns = new Map<string, PolarisColumn>()

	static override get styles() {
		return css`
			:host {
				display: block;
				height: 100%;
				min-height: 0;
			}

			/* Mitra's design tokens, taken from the host theme; the embed reads them off its own element. */
			mitra-embed {
				display: block;
				height: 100%;
				color-scheme: inherit;
				--color-accent: var(--p7t-accent-polaris, var(--interactive-accent));
				--color-background: transparent;
				--color-surface: var(--background-secondary);
				--color-text: var(--text-normal);
				--color-text-muted: var(--text-muted);
				--color-border: var(--background-modifier-border);
			}

			.loading {
				display: flex;
				align-items: center;
				justify-content: center;
				height: 100%;
				color: var(--text-muted);
			}
		`
	}

	protected override connected() {
		void loadMitraEmbed().then(() => this.ready = true)
	}

	protected override willUpdate() {
		this.shownCycles = this.cyclesRef.value ?? this.shownCycles
	}

	/**
	 * The strip's hook for the host content of one day: that day's column, made the first time the day is asked for
	 * and kept (the view keeps it too, so it survives the strip's re-renders and scrolls).
	 */
	private readonly dayFill = (date: Date) => {
		const day = isoDay(date)
		let column = this.columns.get(day)
		if (!column) {
			column = document.createElement('p7t-polaris-column')
			column.day = day
			this.columns.set(day, column)
		}

		this.assign(column, this.plan)
		return column
	}

	/** Which cycle each day holds, and how far planning is offered. */
	private get plan() {
		const byDay = new Map<string, PolarisCycle>()
		for (const cycle of this.shownCycles) {
			if (cycle.day) {
				byDay.set(cycle.day, cycle)
			}
		}

		return { byDay, horizon: forecastHorizon(this.today, byDay.keys(), ForecastView.visibleDays) }
	}

	private assign(column: PolarisColumn, plan: { byDay: Map<string, PolarisCycle>, horizon: string }) {
		const day = column.day!
		column.data = plan.byDay.get(day)
		column.plannable = day >= this.today && day <= plan.horizon
		column.collapsed = this.collapsed
	}

	/** A column's chevron asks for the row to collapse or open: the state is the row's, so every column follows. */
	private onCollapseToggle(e: Event) {
		e.stopPropagation()
		this.collapsed = !this.collapsed
	}

	protected override updated() {
		const plan = this.plan
		for (const column of this.columns.values()) {
			this.assign(column, plan)
		}
	}

	private onRangeChange(e: CustomEvent<MitraRange>) {
		e.stopPropagation()
		this.from = isoDay(e.detail.start)
	}

	protected override get template() {
		return !this.ready ? html`<div class='loading'>Charting the days ahead…</div>` : html`
			<mitra-embed
				view='week-all-day'
				.dayCount=${ForecastView.visibleDays}
				.date=${this.start}
				.dayFill=${this.dayFill}
				@range-change=${(e: CustomEvent<MitraRange>) => this.onRangeChange(e)}
				@collapsetoggle=${(e: Event) => this.onCollapseToggle(e)}>
			</mitra-embed>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-forecast-view': ForecastView
	}
}
