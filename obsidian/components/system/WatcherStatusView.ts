import { Component, component, css, html, nothing, state } from "@a11d/lit"
import { core, IconName } from ".."
import { tooltip } from "../design/Tooltip"

type WatcherIssueReport = NonNullable<Awaited<ReturnType<typeof core.system.getWatcherIssues>>>
type WatcherIssueRecord = WatcherIssueReport["issues"][number]

const POLL_INTERVAL_MS = 5000

const HEALTH_LABELS: Record<string, string> = {
	ok: "Healthy",
	standby: "Suspended",
	issues: "Issues",
	offline: "Offline",
}

/**
 * Status-bar indicator for core/watcher health (PEP108). A colored dot reflects the rolled-up health
 * (ok / standby / issues / offline), and clicking it opens an interactive popover listing the active statuses. Each
 * issue can be dismissed (PEP108 dismiss feature) — snoozed until a different problem arises — and dismissed issues
 * move to a "Dismissed" section where they can be restored. The dot and the live count exclude dismissed issues; the
 * component polls the operation-status report and both the dot and the popover read from it.
 */
@component('p7t-watcher-status')
export class WatcherStatusView extends Component {
	@state() private report?: WatcherIssueReport
	@state() private busyKeys: ReadonlySet<string> = new Set()
	private intervalId?: number

	override connectedCallback() {
		super.connectedCallback()
		void this.refresh()
		this.intervalId = window.setInterval(() => void this.refresh(), POLL_INTERVAL_MS)
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		if (this.intervalId) {
			clearInterval(this.intervalId)
			this.intervalId = undefined
		}
	}

	public async refresh(): Promise<void> {
		try {
			const report = await core.system.getWatcherIssues()
			this.report = report ?? undefined
		}
		catch {
			this.report = undefined
		}
	}

	static override get styles() {
		return css`
			:host { display: inline-flex; align-items: center; }

			.indicator { display: inline-flex; align-items: center; gap: .35em; }
			.dot {
				width: 1.6em;
				height: 1.6em;
				margin-block: -.25em;
				margin-inline: 0 .1em;
				color: var(--dot-color, var(--text-muted));
			}
			.indicator.ok { --dot-color: var(--color-green); }
			.indicator.standby { --dot-color: var(--color-red); }
			.indicator.issues { --dot-color: var(--color-yellow); }
			.indicator.offline { --dot-color: var(--text-faint); }
			.count { font-variant-numeric: tabular-nums; font-size: .85em; color: var(--text-muted); }

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
			.badge.critical, .badge.error { color: var(--color-red); }
			.badge.suspended, .badge.warning { color: var(--color-yellow); }
			.badge.info { color: var(--text-muted); }

			.msg { min-width: 0; }
			.path { grid-column: 2; font-size: .82em; color: var(--text-muted); word-break: break-all; }

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
		const status = this.report?.status ?? "offline"
		const issues = this.report?.issues ?? []
		const live = issues.filter(issue => !issue.dismissed)
		const dismissed = issues.filter(issue => issue.dismissed)
		const label = HEALTH_LABELS[status] ?? status

		return html`
			<p7t-popover placement="top">
				<span class="indicator ${status}">
					<p7t-icon class='dot' icon='watcher'></p7t-icon>
					${live.length > 0 ? html`<span class="count">${live.length}</span>` : nothing}
				</span>
				<div slot="content" class="body">
					<div class="title">Watcher <span class="status ${status}">${label}</span></div>
					${live.length === 0 && dismissed.length === 0
						? status === "offline" ? html`<div class="empty">The PLAINTORCH core is offline for this user and vault.</div>` : html`<div class="empty">No active issues.</div>`
						: nothing}
					${live.map(issue => this.renderIssue(issue))}
					${dismissed.length > 0
						? html`
							<div class="dismissed-header">Dismissed · ${dismissed.length}</div>
							${dismissed.map(issue => this.renderIssue(issue))}
						`
						: nothing}
				</div>
			</p7t-popover>
		`
	}

	private renderIssue(issue: WatcherIssueRecord) {
		const path = issue.originVaultRelativePath ?? issue.files[0]
		const busy = this.busyKeys.has(issue.key)
		return html`
			<div class="issue ${issue.dismissed ? 'is-dismissed' : ''}">
				<span class="badge ${issue.severity}" ${tooltip(this.severityName(issue.severity))}>
					<p7t-icon icon=${this.severityIcon(issue.severity)}></p7t-icon>
				</span>
				<span class="msg">${issue.message}</span>
				<p7t-button
					class='dismiss-button'
					ghost
					danger
					?disabled=${busy}
					@click=${() => void this.toggleDismissal(issue)}
					icon=${issue.dismissed ? 'lucide:undo-2' : 'lucide:ban'}
					label=${issue.dismissed ? 'Undo' : 'Dismiss'}>
				</p7t-button>
				${path ? html`<span class="path">${path.replaceAll("\\", "/")}</span>` : nothing}
			</div>
		`
	}

	private severityIcon(severity: string): IconName {
		switch (severity) {
			case 'info': return 'lucide:info';

			case 'warning': return 'lucide:circle-alert';
			case 'suspended': return 'lucide:circle-pause';

			case 'error': return 'lucide:octagon-alert';
			case 'critical': return 'lucide:octagon-x';

			default: return 'lucide:badge-question-mark';
		}
	}

	private severityName(severity: string): string {
		switch (severity) {
			case 'info': return 'Info';

			case 'warning': return 'Warning';
			case 'suspended': return 'Suspended';

			case 'error': return 'Error';
			case 'critical': return 'Critical';
			default: return 'Unknown';
		}
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
			await this.refresh()
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

declare global {
	interface HTMLElementTagNameMap {
		'p7t-watcher-status': WatcherStatusView
	}
}
