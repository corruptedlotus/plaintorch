import { Component, component, css, html, nothing, property, state } from "@a11d/lit"
import { core } from "../data"
import { tooltip } from "../design/Tooltip"
import type { IconName } from "../PleiadesIcon"
import { refreshWatcherReport, type WatcherIssueRecord, type WatcherIssueReport } from "./watcherReport"

/**
 * The watcher's issues (PEP108), worst first as the core sorts them: each live one with its severity badge — info
 * muted, warning and error yellow, critical orange, fatal red (a fatal issue puts the watcher on standby) — message,
 * detail and file, then a "Dismissed" section. An issue short of fatal can be dismissed — snoozed until a different
 * problem arises — and a dismissed one restored; either way the report is fetched again, so every surface showing it
 * agrees. A fatal issue offers no dismissal: the core refuses one, since the watcher cannot run until it clears. With
 * no report it says the core is offline, and with nothing to list, that the watcher is on standby or that there are no
 * active issues. The status-bar drawer and the status card both show it.
 */
@component('p7t-watcher-issue-list')
export class WatcherIssueList extends Component {
	@property({ attribute: false }) report?: WatcherIssueReport
	@state() private busyKeys: ReadonlySet<string> = new Set()

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .5em;
			}

			.empty { color: var(--text-muted); }

			.issue {
				display: grid;
				grid-template-columns: auto 1fr auto;
				gap: .15em .5em;
				align-items: flex-start;
			}
			.issue.is-dismissed { opacity: .6; }
			.badge {
				justify-self: start;
				text-transform: uppercase;
				letter-spacing: .05em;
				font-weight: 700;
				padding: .12em;
				display: inline-flex;
				flex-direction: column;
				align-items: center;
				gap: .1em;

				& p7t-icon { font-size: 1.4em; }
			}
			.badge.info { color: var(--text-muted); }
			.badge.warning, .badge.error { color: var(--color-yellow); }
			.badge.critical { color: var(--color-orange); }
			.badge.fatal { color: var(--color-red); }

			.msg { min-width: 0; }

			.path { grid-column: 2; font-size: .8em; color: var(--text-accent); word-break: break-all; opacity: .7 }
			.detail { grid-column: 2; font-size: .86em; color: var(--text-muted); }

			.dismissed-header {
				margin-top: .1em;
				padding-top: .5em;
				border-top: 1px solid color-mix(in srgb, var(--text-normal) 12%, transparent);
				font-size: .78em;
				text-transform: uppercase;
				letter-spacing: .04em;
				color: var(--text-faint);
			}
		`
	}

	protected override get template() {
		const issues = this.report?.issues ?? []
		const live = issues.filter(issue => !issue.dismissed)
		const dismissed = issues.filter(issue => issue.dismissed)
		return html`
			${live.length === 0 && dismissed.length === 0 ? this.emptyTemplate : nothing}
			${live.map(issue => this.renderIssue(issue))}
			${dismissed.length > 0
				? html`
					<div class="dismissed-header">Dismissed · ${dismissed.length}</div>
					${dismissed.map(issue => this.renderIssue(issue))}
				`
				: nothing}
		`
	}

	/** What stands in for the list when there is nothing to list: why, as far as the report tells. */
	private get emptyTemplate() {
		switch (this.report?.status ?? "offline") {
			case "offline": return html`<div class="empty">The PLAINTORCH core is offline for this user and vault.</div>`
			case "standby": return html`<div class="empty">The watcher is on standby.</div>`
			default: return html`<div class="empty">No active issues.</div>`
		}
	}

	private renderIssue(issue: WatcherIssueRecord) {
		const path = issue.originVaultRelativePath ?? issue.files[0]
		const busy = this.busyKeys.has(issue.key)
		// The core refuses to dismiss a fatal issue; restoring a dismissed one is always offered.
		const toggleable = issue.dismissed || issue.severity !== 'fatal'
		return html`
			<div class="issue ${issue.dismissed ? 'is-dismissed' : ''}">
				<span class="badge ${issue.severity}" ${tooltip(severityName(issue.severity))}>
					<p7t-icon icon=${severityIcon(issue.severity)}></p7t-icon>
				</span>
				<span class="msg">${issue.message}</span>
				${toggleable ? html`
					<p7t-button
						class='dismiss-button'
						ghost
						danger
						?disabled=${busy}
						@click=${() => void this.toggleDismissal(issue)}
						icon=${issue.dismissed ? 'lucide:undo-2' : 'lucide:ban'}
						label=${issue.dismissed ? 'Undo' : 'Dismiss'}>
					</p7t-button>
				` : nothing}
				${issue.detail ? html`<span class="detail">${issue.detail}</span>` : nothing}
				${path ? html`<span class="path">${path.replaceAll("\\", "/")}</span>` : nothing}
			</div>
		`
	}

	/** Dismisses a live issue or restores a dismissed one, then refreshes so the split reflects the new state. */
	private async toggleDismissal(issue: WatcherIssueRecord): Promise<void> {
		if (this.busyKeys.has(issue.key)) return
		this.busyKeys = new Set(this.busyKeys).add(issue.key)
		try {
			if (issue.dismissed) {
				await core.system.restoreWatcherIssue(issue.key)
			}
			else {
				await core.system.dismissWatcherIssue(issue.key)
			}
			await refreshWatcherReport()
		}
		catch {
			// Leave state unchanged; the next poll reconciles.
		}
		finally {
			const next = new Set(this.busyKeys)
			next.delete(issue.key)
			this.busyKeys = next
		}
	}
}

/** A severity's badge glyph; `fatal` shows a pause, since a fatal issue puts the watcher on standby. */
function severityIcon(severity: WatcherIssueRecord["severity"]): IconName {
	switch (severity) {
		case 'info': return 'lucide:info'
		case 'warning': return 'lucide:circle-alert'
		case 'error': return 'lucide:octagon-alert'
		case 'critical': return 'lucide:octagon-x'
		case 'fatal': return 'lucide:circle-pause'
		default: return 'lucide:badge-question-mark'
	}
}

/** A severity's display name, the badge's tooltip. */
function severityName(severity: WatcherIssueRecord["severity"]): string {
	switch (severity) {
		case 'info': return 'Info'
		case 'warning': return 'Warning'
		case 'error': return 'Error'
		case 'critical': return 'Critical'
		case 'fatal': return 'Fatal'
		default: return 'Unknown'
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-watcher-issue-list': WatcherIssueList
	}
}
