import { Component, component, css, html, state } from "@a11d/lit"
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
 * (ok / standby / issues / offline), and a hover tooltip lists the active statuses with their severity. The
 * component polls the operation-status report and both the dot and the tooltip read from it.
 */
@component('p7t-watcher-status')
export class WatcherStatusView extends Component {
	@state() private report?: WatcherIssueReport
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

			.indicator { display: inline-flex; align-items: center; gap: .35em; cursor: default; }
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

			.body { display: flex; flex-direction: column; gap: .45em; min-width: 13em; max-width: 22em; }
			.title { font-weight: 600; }
			.empty { color: var(--text-muted); }
			.issue { display: grid; grid-template-columns: auto 1fr; gap: .1em .5em; align-items: baseline; }
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
		`
	}

	protected override get template() {
		const status = this.report?.status ?? "ok"
		const issues = this.report?.issues ?? []
		const label = HEALTH_LABELS[status] ?? status

		return html`
			<p7t-tooltip .showDelay=${120}>
				<span class="indicator ${status}">
					<span class="dot"></span>
					${issues.length > 0 ? html`<span class="count">${issues.length}</span>` : ''}
				</span>
				<div slot="tooltip" class="body">
					<div class="title">Watcher · ${label}</div>
					${issues.length === 0
						? html`<div class="empty">No active issues.</div>`
						: issues.map(issue => this.renderIssue(issue))}
				</div>
			</p7t-tooltip>
		`
	}

	private renderIssue(issue: WatcherIssueRecord) {
		const path = issue.originVaultRelativePath ?? issue.files[0]
		return html`
			<div class="issue">
				<span class="badge ${issue.severity}">${issue.severity}</span>
				<span class="msg">${issue.message}</span>
				${path ? html`<span class="path">${path.replaceAll("\\", "/")}</span>` : ""}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-watcher-status': WatcherStatusView
	}
}
