import { Component, component, css, html, nothing } from "@a11d/lit"
import { ActiveTimeframesRef } from ".."

/**
 * The timeframe(s) in play right now, as small chips. Nothing renders while no timeframe is active.
 *
 * The core decides which ones those are (PEP100 patch 2), so this element only draws: it reads the active timeframes
 * of the strictly active Polaris cycle, which the core picks from the cycle's cached candidates — the timeframes
 * whose orbit matches the cycle's day (no orbit is every day) — narrowed to those whose window holds the current
 * minute (an overnight window wraps midnight), and, when any of those is exclusive, to the exclusive ones alone.
 *
 * The listing is the shared {@link ActiveTimeframesRef} record, the same one an affined executive reads to decide
 * whether it flares, so a chip and a glowing row always agree. It re-reads on the shared 60-second tick, so a chip
 * can appear or vanish up to about a minute after its window edge.
 */
@component('p7t-active-timeframes')
export class ActiveTimeframesItem extends Component {
	/** The active timeframes, in the core's order. */
	private readonly active = new ActiveTimeframesRef(this)

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

	/** One small chip per active timeframe; nothing while none is active. */
	protected override get template() {
		const timeframes = this.active.timeframes
		return html`
			${timeframes.length === 0 ? nothing : timeframes.map(timeframe => html`<p7t-timeframe-item small .timeframe=${timeframe}></p7t-timeframe-item>`)}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-active-timeframes': ActiveTimeframesItem
	}
}
