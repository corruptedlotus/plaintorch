import { component, css, html } from "@a11d/lit"
import { CardComponent } from "../design/CardComponent"
import { WatcherReportController, watcherHealthLabel, type WatcherHealth, type WatcherIssueRecord } from "./watcherReport"

/**
 * The watcher's state as a card (PEP108): what the status-bar indicator shows, spelled out. The header carries the
 * indicator — the glyph in its health's colour and the live count — with the health (healthy, issues, critical,
 * standby or offline) and a line on what it means (how many issues, how many critical or fatal, how many dismissed);
 * below it, the {@link WatcherIssueList} the status-bar drawer shows. It reads the document's shared poll of the report.
 */
@component('p7t-watcher-status-card')
export class WatcherStatusCard extends CardComponent {
	private readonly watcher = new WatcherReportController(this)

	static override get styles() {
		return css`
			${super.styles}

			.watcher-header {
				display: flex;
				align-items: center;
				gap: .9em;
				padding-inline: .5em;
				margin-block: .1em .4em;
			}

			p7t-watcher-indicator {
				font-size: 1.6em;
			}

			.watcher-header [part=header] {
				padding-inline: 0;
				margin-block: 0;
			}

			p7t-watcher-issue-list {
				padding-inline: .5em;
			}
		`
	}

	protected override get headerTemplate() {
		const health = this.watcher.health
		return html`
			<div class='watcher-header'>
				<p7t-watcher-indicator .health=${health} .count=${this.watcher.live.length}></p7t-watcher-indicator>
				<div part='header'>
					<span part='pre-heading'>Watcher</span>
					<span part='heading'>${watcherHealthLabel(health)}</span>
					<span part='sub-heading'>${describe(health, this.watcher.live, this.watcher.dismissed)}</span>
				</div>
			</div>
		`
	}

	protected override get content() {
		return html`<p7t-watcher-issue-list .report=${this.watcher.report}></p7t-watcher-issue-list>`
	}
}

function plural(count: number, noun: string): string {
	return `${count} ${noun}${count === 1 ? '' : 's'}`
}

/**
 * A sentence on what a health means with these issues, then the counts: the live issues — every one the list shows,
 * `info` included though it never affects health, so the count agrees with the indicator's — how many of them are
 * critical (a `critical` or `fatal` severity, the record's `isCritical`), and how many are dismissed.
 */
function describe(health: WatcherHealth, live: readonly WatcherIssueRecord[], dismissed: readonly WatcherIssueRecord[]): string {
	const critical = live.filter(issue => issue.isCritical).length
	const counts = [
		live.length > 0 ? plural(live.length, 'active issue') : 'No active issues',
		critical > 0 ? `${critical} critical` : undefined,
		dismissed.length > 0 ? `${dismissed.length} dismissed` : undefined,
	].filter(part => part !== undefined).join(' · ')

	switch (health) {
		case 'ok':
			return `Watching the vault. ${counts}.`
		case 'issues':
			return `Watching the vault, with problems to look at. ${counts}.`
		case 'critical':
			return `Watching the vault, but part of its work is blocked. ${counts}.`
		case 'standby':
			return live.length > 0
				? `On standby: a fatal problem stops the watcher (see below). ${counts}.`
				: 'On standby: no vault is being watched.'
		case 'offline':
			return 'No report — the PLAINTORCH core is offline, or serves no vault.'
		default:
			return counts
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-watcher-status-card': WatcherStatusCard
	}
}
