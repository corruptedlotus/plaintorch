import { Component, component, css, html, nothing, property } from "@a11d/lit"
import type { WatcherHealth } from "./watcherReport"

/**
 * The watcher's glyph in its health's colour — green healthy, yellow with issues, orange critical (part of its work
 * blocked), red on standby (not watching), faint offline — and the count of live issues beside it when there are any
 * (PEP108). The status-bar indicator and the status card's header both draw it; its size follows the surrounding font.
 */
@component('p7t-watcher-indicator')
export class WatcherIndicator extends Component {
	@property({ reflect: true }) health: WatcherHealth = "offline"
	@property({ type: Number }) count = 0

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				gap: .35em;
				--p7t-watcher-color: var(--text-muted);
			}

			:host([health=ok]) { --p7t-watcher-color: var(--color-green); }
			:host([health=issues]) { --p7t-watcher-color: var(--color-yellow); }
			:host([health=critical]) { --p7t-watcher-color: var(--color-orange); }
			:host([health=standby]) { --p7t-watcher-color: var(--color-red); }
			:host([health=offline]) { --p7t-watcher-color: var(--text-faint); }

			.dot {
				width: 1.6em;
				height: 1.6em;
				margin-block: -.25em;
				margin-inline: 0 .1em;
				color: var(--p7t-watcher-color);
			}

			.count {
				font-variant-numeric: tabular-nums;
				font-size: .85em;
				color: var(--text-muted);
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-icon class='dot' part='glyph' icon='watcher'></p7t-icon>
			${this.count > 0 ? html`<span class='count' part='count'>${this.count}</span>` : nothing}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-watcher-indicator': WatcherIndicator
	}
}
