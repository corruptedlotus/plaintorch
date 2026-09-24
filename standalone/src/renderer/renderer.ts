import { Component, component, css, html, nothing, state, unsafeCSS } from "@a11d/lit"
import { PlaintorchCoreClient, type SystemBriefing } from "@pleiades/sdk/plaintorch"
import type { CorePhase, PlaintorchBridge, ShellStatus } from "../shared/contracts"
import { BridgeTransport } from "./bridgeTransport"
import splashArtwork from "../../assets/splash-loading.png"

declare global {
	interface Window {
		plaintorch: PlaintorchBridge
	}
}

const bridge = window.plaintorch

/** The status window's SDK client, for its vault summary. */
const core = new PlaintorchCoreClient({ transports: [new BridgeTransport(bridge)] })

const phaseTone: Record<CorePhase, "ok" | "warn" | "bad" | "muted"> = {
	Starting: "warn",
	Activating: "warn",
	Active: "ok",
	Idle: "muted",
	Failed: "bad",
	Stopping: "muted"
}

function phaseLabel(status: ShellStatus): string {
	if (status.attachment === "absent") {
		return "No core"
	}

	if (status.attachment === "exited") {
		return "Core exited"
	}

	if (status.sweeping) {
		return "Sweeping"
	}

	return status.phase
}

/** Routes the window to the splash or the status view from the URL hash. */
@component("p7t-app")
export class App extends Component {
	@state() private status?: ShellStatus
	private unsubscribe?: () => void

	private get view(): "splash" | "status" {
		return location.hash === "#splash" ? "splash" : "status"
	}

	override connectedCallback() {
		super.connectedCallback()
		document.body.classList.toggle("status", this.view === "status")
		this.unsubscribe = bridge.onStatus(status => {
			this.status = status
		})
		void bridge.getStatus().then(status => {
			this.status = status
		})
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		this.unsubscribe?.()
	}

	protected override get template() {
		if (!this.status) {
			return html``
		}

		return this.view === "splash"
			? html`<p7t-splash .status=${this.status}></p7t-splash>`
			: html`<p7t-status .status=${this.status}></p7t-status>`
	}
}

/** The frameless startup popup: the artwork with the core's latest phase line over it. */
@component("p7t-splash")
export class Splash extends Component {
	@state() status!: ShellStatus

	static override get styles() {
		return css`
			:host {
				display: block;
				position: relative;
				width: 943px;
				height: 405px;
				overflow: hidden;
				border-radius: 6px;
				background: url(${unsafeCSS(splashArtwork)}) center / cover no-repeat;
				background-color: transparent;
				/*box-shadow: 0 18px 48px rgba(0, 0, 0, 0.55);*/
				font-family: "Space Grotesk", "Segoe UI", system-ui, sans-serif;
			}
			.message {
				position: absolute;
				left: 32px;
				top: 24px;
				max-width: 520px;
				color: #f6f1ea;
				font-size: 14px;
				white-space: pre-wrap;
				text-shadow: 0 1px 2px rgba(0, 0, 0, 0.6);
			}
			.message.failed { color: #f7dee1; font-size: 16px; font-weight: 300; }
			.actions { position: absolute; right: 24px; bottom: 20px; display: flex; gap: 8px; }
			button {
				border: 1px solid rgba(255, 255, 255, 0.35);
				background: rgba(0, 0, 0, 0.35);
				color: #f6f1ea;
				border-radius: 4px;
				padding: 6px 14px;
				cursor: pointer;
				font: inherit;
			}
			button:hover { background: rgba(0, 0, 0, 0.55); }
		`
	}

	protected override get template() {
		const failed = this.status.phase === "Failed" || this.status.attachment === "exited"
		return html`
			<style>:host { background-image: url(${splashArtwork}); }</style>
			<div class="message ${failed ? "failed" : ""}">${this.status.message ?? "Starting PLAINTORCH core..."}</div>
			${failed ? html`
				<div class="actions">
					<button @click=${() => bridge.openStatus()}>Open status</button>
					<button @click=${() => bridge.closeSplash()}>Close</button>
				</div>
			` : nothing}
		`
	}
}

/** The status window: core phase and attachment, vault, endpoint, host settings, and a briefing of the vault. */
@component("p7t-status")
export class Status extends Component {
	@state() status!: ShellStatus
	@state() private briefing?: SystemBriefing
	@state() private briefingError?: string
	private lastBriefingKey?: string

	static override get styles() {
		return css`
			:host { display: block; padding: 24px 28px 32px; }
			header { display: flex; align-items: center; gap: 14px; margin-bottom: 20px; }
			header h1 { margin: 0; font-size: 20px; letter-spacing: 0.08em; }
			header .flavor { color: var(--p7t-muted); font-size: 12px; }
			.badge { padding: 3px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.06em; }
			.badge.ok { background: rgba(124, 196, 143, 0.18); color: var(--p7t-ok); }
			.badge.warn { background: rgba(230, 180, 90, 0.18); color: var(--p7t-warn); }
			.badge.bad { background: rgba(226, 109, 109, 0.18); color: var(--p7t-bad); }
			.badge.muted { background: rgba(168, 156, 164, 0.18); color: var(--p7t-muted); }
			section { background: var(--p7t-panel); border: 1px solid var(--p7t-line); border-radius: 8px; padding: 16px 18px; margin-bottom: 14px; }
			section h2 { margin: 0 0 10px; font-size: 12px; text-transform: uppercase; letter-spacing: 0.1em; color: var(--p7t-muted); }
			dl { display: grid; grid-template-columns: 140px 1fr; gap: 6px 14px; margin: 0; }
			dt { color: var(--p7t-muted); }
			dd { margin: 0; word-break: break-all; }
			.message { white-space: pre-wrap; }
			.actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
			button {
				border: 1px solid var(--p7t-line);
				background: #352c3a;
				color: var(--p7t-text);
				border-radius: 6px;
				padding: 7px 14px;
				cursor: pointer;
				font: inherit;
			}
			button:hover:not(:disabled) { border-color: var(--p7t-accent); }
			button:disabled { opacity: 0.5; cursor: default; }
			button.primary { background: var(--p7t-accent); border-color: var(--p7t-accent); color: #ffffff; font-weight: 600; }
			label.toggle { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; }
			.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; }
			.card { background: rgba(0, 0, 0, 0.18); border-radius: 6px; padding: 10px 12px; }
			.card .label { color: var(--p7t-muted); font-size: 12px; }
			.card .value { font-size: 18px; font-weight: 600; margin-top: 2px; }
			.hint { color: var(--p7t-muted); font-size: 12px; }
		`
	}

	override updated(changed: Map<PropertyKey, unknown>) {
		super.updated(changed)
		if (changed.has("status")) {
			void this.refreshBriefing()
		}
	}

	private async refreshBriefing() {
		const key = `${this.status.attachment}|${this.status.phase}|${this.status.sweeping}|${this.status.vault}`
		if (key === this.lastBriefingKey) {
			return
		}

		this.lastBriefingKey = key
		// Wait for the startup sweep to finish before reading vault data, so the briefing reflects a settled vault.
		if (this.status.phase !== "Active" || this.status.sweeping) {
			this.briefing = undefined
			this.briefingError = undefined
			return
		}

		try {
			this.briefing = await core.system.getBriefing()
			this.briefingError = this.briefing ? undefined : "The core did not answer the briefing request."
		}
		catch (error) {
			this.briefingError = error instanceof Error ? error.message : String(error)
		}
	}

	protected override get template() {
		const status = this.status
		const tone = status.attachment === "absent" || status.attachment === "exited"
			? "bad"
			: status.sweeping ? "warn" : phaseTone[status.phase]
		return html`
			<header>
				<h1>PLAINTORCH</h1>
				<span class="badge ${tone}">${phaseLabel(status)}</span>
				<span class="flavor">${status.flavor} · v${status.version}</span>
			</header>

			<section>
				<h2>Core</h2>
				<dl>
					<dt>Attachment</dt><dd>${this.describeAttachment()}</dd>
					<dt>Message</dt><dd class="message">${status.message ?? "—"}</dd>
					<dt>Vault</dt><dd>${status.vault ?? status.activeVaultSetting ?? "none"}</dd>
					<dt>Endpoint</dt><dd>${status.endpoint}</dd>
					<dt>Profile</dt><dd>${status.profile}</dd>
					${status.pid ? html`<dt>Process</dt><dd>${status.pid}</dd>` : nothing}
				</dl>
				<div class="actions">
					<button class="primary" @click=${() => bridge.openBriefing()}>Open briefing</button>
					<button @click=${() => bridge.activateVault()}>Activate vault…</button>
					<button ?disabled=${!status.activeVaultSetting} @click=${() => bridge.deactivateVault()}>Deactivate vault</button>
					${status.flavor === "standalone" ? html`<button @click=${() => bridge.restartCore()}>Restart core</button>` : nothing}
					<button @click=${() => bridge.openLogs()}>Open logs</button>
				</div>
			</section>

			<section>
				<h2>Shell</h2>
				<div class="actions">
					<label class="toggle">
						<input type="checkbox" .checked=${status.autostart} @change=${(event: Event) => bridge.setAutostart((event.target as HTMLInputElement).checked)}>
						Start at login
					</label>
					${this.updateTemplate()}
					<button @click=${() => bridge.quit()}>Quit PLAINTORCH</button>
				</div>
			</section>

			${this.briefingTemplate()}
		`
	}

	private describeAttachment(): string {
		switch (this.status.attachment) {
			case "spawned": return "core hosted by this app"
			case "attached": return "attached to an external core"
			case "absent": return "no core reachable"
			case "exited": return `core exited${this.status.exitCode !== undefined ? ` (code ${this.status.exitCode})` : ""}`
			default: return "starting"
		}
	}

	private updateTemplate() {
		const update = this.status.updateState
		switch (update.kind) {
			case "available":
				return html`<button class="primary" @click=${() => bridge.installUpdate()}>Download update ${update.version}</button>`
			case "ready":
				return html`<button class="primary" @click=${() => bridge.installUpdate()}>Install ${update.version} and restart</button>`
			case "downloading":
				return html`<button disabled>Downloading… ${update.percent}%</button>`
			case "checking":
				return html`<button disabled>Checking…</button>`
			case "unavailable":
				return html`<button disabled title=${update.reason}>Check for updates</button>`
			case "error":
				return html`<button @click=${() => bridge.checkForUpdates()} title=${update.message}>Retry update check</button>`
			case "none":
				return html`<button @click=${() => bridge.checkForUpdates()}>Up to date (${update.version})</button>`
			default:
				return html`<button @click=${() => bridge.checkForUpdates()}>Check for updates</button>`
		}
	}

	private briefingTemplate() {
		if (this.status.sweeping) {
			return html`<section><h2>Vault</h2><div class="hint">Running the startup sweep… vault data appears once it finishes.</div></section>`
		}

		if (this.status.phase !== "Active") {
			return html`<section><h2>Vault</h2><div class="hint">Vault data appears once a vault is active.</div></section>`
		}

		if (this.briefingError) {
			return html`<section><h2>Vault</h2><div class="hint">${this.briefingError}</div></section>`
		}

		const briefing = this.briefing
		if (!briefing) {
			return html`<section><h2>Vault</h2><div class="hint">Loading…</div></section>`
		}

		return html`
			<section>
				<h2>Vault</h2>
				<div class="cards">
					<div class="card"><div class="label">Pleiadean today</div><div class="value">${briefing.pleiadeanToday}</div></div>
					<div class="card"><div class="label">Celestron banked</div><div class="value">${briefing.celestronBanked}</div></div>
					<div class="card"><div class="label">Watcher</div><div class="value">${briefing.watcherStatus}</div></div>
					<div class="card"><div class="label">Watcher issues</div><div class="value">${briefing.watcherIssueCount}${briefing.watcherCriticalIssueCount ? html` <span class="hint">(${briefing.watcherCriticalIssueCount} critical)</span>` : nothing}</div></div>
					<div class="card"><div class="label">Current Onrush</div><div class="value">${briefing.currentOnrush?.title ?? "none"}</div></div>
					<div class="card"><div class="label">Current Polaris</div><div class="value">${briefing.currentPolaris?.title ?? "none"}</div></div>
				</div>
			</section>
		`
	}
}
