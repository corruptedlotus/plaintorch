import { Component, component, css, html, nothing, state } from "@a11d/lit"
import { core } from ".."

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

	/** Fetches the current watcher status report. A transient failure keeps the last-known status on screen. */
	public async refresh(): Promise<void> {
		try {
			const report = await core.system.getWatcherIssues()
			if (report) {
				this.report = report
			}
		}
		catch {
			// Leave the last-known status rather than blanking the indicator on a dropped request.
		}
	}

	static override get styles() {
		return css`
			:host { display: inline-flex; align-items: center; }

			.indicator { display: inline-flex; align-items: center; gap: .35em; }
			.dot {
				width: .7em;
				height: .7em;
				border-radius: 50%;
				background-color: var(--dot-color, var(--text-muted));
				box-shadow: 0 0 0 1px color-mix(in srgb, var(--dot-color, var(--text-muted)) 45%, transparent);
			}
			.indicator.ok { --dot-color: var(--color-green); }
			.indicator.standby { --dot-color: var(--color-yellow); }
			.indicator.issues { --dot-color: var(--color-red); }
			.indicator.offline { --dot-color: var(--text-faint); }
			.indicator.offline .dot { background-color: transparent; box-shadow: inset 0 0 0 1.5px var(--text-faint); }
			.count { font-variant-numeric: tabular-nums; font-size: .85em; color: var(--text-muted); }

			.body { display: flex; flex-direction: column; gap: .5em; min-width: 15em; }
			.title { font-weight: 600; }
			.empty { color: var(--text-muted); }

			.issue {
				display: grid;
				grid-template-columns: auto 1fr auto;
				gap: .15em .5em;
				align-items: baseline;
			}
			.issue.is-dismissed { opacity: .6; }
			.badge {
				justify-self: start;
				text-transform: uppercase;
				font-size: .62em;
				letter-spacing: .05em;
				font-weight: 700;
				padding: .12em .45em;
				border-radius: 5px;
				color: var(--badge-fg, var(--text-on-accent, #fff));
				background-color: var(--badge-bg, var(--text-muted));
			}
			.badge.critical, .badge.error { --badge-bg: var(--color-red); }
			.badge.suspended, .badge.warning { --badge-bg: var(--color-yellow); --badge-fg: #000; }
			.badge.info { --badge-bg: var(--text-muted); }
			.msg { min-width: 0; }
			.path { grid-column: 2; font-size: .82em; color: var(--text-muted); word-break: break-all; }

			.action {
				grid-row: 1;
				grid-column: 3;
				align-self: center;
				padding: .12em .5em;
				border: 1px solid color-mix(in srgb, var(--text-normal) 20%, transparent);
				border-radius: 6px;
				background: transparent;
				color: var(--text-muted);
				font-family: inherit;
				font-size: .72em;
				cursor: pointer;
				white-space: nowrap;
			}
			.action:hover:not(:disabled) { color: var(--text-normal); background-color: color-mix(in srgb, var(--text-normal) 8%, transparent); }
			.action:disabled { opacity: .5; cursor: default; }

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
		const status = this.report?.status ?? "ok"
		const issues = this.report?.issues ?? []
		const live = issues.filter(issue => !issue.dismissed)
		const dismissed = issues.filter(issue => issue.dismissed)
		const label = HEALTH_LABELS[status] ?? status

		return html`
			<p7t-popover placement="top">
				<span class="indicator ${status}">
					<span class="dot"></span>
					${live.length > 0 ? html`<span class="count">${live.length}</span>` : nothing}
				</span>
				<div slot="content" class="body">
					<div class="title">Watcher · ${label}</div>
					${live.length === 0 && dismissed.length === 0
						? html`<div class="empty">No active issues.</div>`
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
				<span class="badge ${issue.severity}">${issue.severity}</span>
				<span class="msg">${issue.message}</span>
				<button
					class="action"
					?disabled=${busy}
					@click=${() => void this.toggleDismissal(issue)}>
					${issue.dismissed ? 'Restore' : 'Dismiss'}
				</button>
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
