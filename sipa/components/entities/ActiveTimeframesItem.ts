import { Component, component, css, html, nothing, state } from "@a11d/lit"
import type { DirectiveTimeframeRecord } from "@pleiades/sdk"
import { core } from ".."
import { subscribeTick } from "../system/globalTick"

/**
 * The timeframe(s) in play right now, as small chips. Nothing renders while no timeframe is active.
 *
 * The core decides which ones those are (PEP100 patch 2), so this element only fetches and draws: it asks for the
 * active timeframes of the strictly active Polaris cycle, which the core picks from the cycle's cached candidates —
 * the timeframes whose orbit matches the cycle's day (no orbit is every day) — narrowed to those whose window holds
 * the current minute (an overnight window wraps midnight), and, when any of those is exclusive, to the exclusive
 * ones alone.
 *
 * It re-reads once on connect and then on the shared 60-second tick, so a chip can appear or vanish up to about a
 * minute after its window edge.
 */
@component('p7t-active-timeframes')
export class ActiveTimeframesItem extends Component {
	/** The active timeframes as the core last returned them, in its order. */
	@state() private timeframes: DirectiveTimeframeRecord[] = []

	/** Ends the 60-second tick subscription; set while connected. */
	private unsubscribeTick?: () => void

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
			}

			p7t-timeframe-item {
				padding-inline: .5ch .8ch;
				padding-block: .2em;
				border-radius: 12px;
				color: var(--text-muted);
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 10%, black);
			}
		`
	}

	/** Reads the active timeframes at once and subscribes to the shared 60-second tick to re-read them. */
	protected override connected() {
		void this.load()
		this.unsubscribeTick?.()
		this.unsubscribeTick = subscribeTick(() => void this.load(), '60s')
	}

	/** Drops the tick subscription. */
	protected override disconnected() {
		this.unsubscribeTick?.()
		this.unsubscribeTick = undefined
	}

	/** Asks the core for the timeframes active right now. */
	private async load() {
		this.timeframes = await core.directives.listActiveTimeframes()
	}

	/** One small chip per active timeframe; nothing while none is active. */
	protected override get template() {
		return html`
			${this.timeframes.length === 0 ? nothing : this.timeframes.map(timeframe => html`<p7t-timeframe-item small .timeframe=${timeframe}></p7t-timeframe-item>`)}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-active-timeframes': ActiveTimeframesItem
	}
}
