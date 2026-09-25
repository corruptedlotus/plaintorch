import { Component, component, css, html } from "@a11d/lit"
import { WatcherReportController, watcherHealthLabel } from "./watcherReport"

/**
 * Status-bar indicator for core/watcher health (PEP108). The watcher's glyph in its health's colour (ok / standby /
 * issues / offline) with the live issue count, and a click opens a popover listing the active statuses — the
 * {@link WatcherIssueList}, where an issue can be dismissed (snoozed until a different problem arises) and a dismissed
 * one restored. The glyph and the count exclude dismissed issues; the report is the document's shared poll.
 */
@component('p7t-watcher-status')
export class WatcherStatusView extends Component {
	private readonly watcher = new WatcherReportController(this)

	static override get styles() {
		return css`
			:host { display: inline-flex; align-items: center; }

			.body { display: flex; flex-direction: column; gap: .5em; min-width: 15em; }
			.title {
				font-weight: 600;
				display: flex;
				align-items: baseline;
				gap: .8ch;

				.status {
					font-weight: 400;
					color: var(--text-muted);
					background-color: color-mix(in srgb, currentColor 10%, transparent);
					padding: .05em .5em;
					border-radius: .25em;

					&.ok {
						color: var(--color-green);
					}
					&.standby {
						color: var(--color-red);
					}
					&.issues {
						color: var(--color-yellow);
					}
					&.offline {
						color: var(--text-faint);
					}
				}
			}
		`
	}

	protected override get template() {
		const health = this.watcher.health
		return html`
			<p7t-popover placement="top">
				<p7t-watcher-indicator .health=${health} .count=${this.watcher.live.length}></p7t-watcher-indicator>
				<div slot="content" class="body">
					<div class="title">Watcher <span class="status ${health}">${watcherHealthLabel(health)}</span></div>
					<p7t-watcher-issue-list .report=${this.watcher.report}></p7t-watcher-issue-list>
				</div>
			</p7t-popover>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-watcher-status': WatcherStatusView
	}
}
