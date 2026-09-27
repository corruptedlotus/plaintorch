import { Component, component, css, html, nothing, state } from "@a11d/lit"
import { PlaintorchCoreClient } from "@pleiades/sdk/plaintorch"
import { provideCore, provideHost, type IconName } from "@pleiades/sipa"
import { createSipaHost } from "@pleiades/sipa/hosts/sipa"
import { mediaScheme, type CorePhase, type PlaintorchBridge, type ShellStatus } from "../shared/contracts"
import { BridgeTransport } from "./bridgeTransport"
import "./WindowFrame"

/**
 * The status window: the core, the shell, and the watcher, drawn with the SIPA pieces — cards, chips, buttons and the
 * theme — under the window's own title bar. The SIPA host and a bridge-backed core client are installed first: the
 * watcher card reads the core's watcher report through them, and the chips and buttons draw their glyphs through the
 * host.
 */

declare global {
	interface Window {
		plaintorch: PlaintorchBridge
	}
}

const bridge = window.plaintorch

// The status window shows no vault images, so the media scheme is never asked for one.
provideHost(createSipaHost({ mediaScheme, mediaUrl: () => undefined }))
provideCore(new PlaintorchCoreClient({ transports: [new BridgeTransport(bridge)] }))

type Tone = "ok" | "warn" | "bad" | "muted"

const phaseTone: Record<CorePhase, Tone> = {
	Starting: "warn",
	Activating: "warn",
	Active: "ok",
	Idle: "muted",
	Failed: "bad",
	Stopping: "muted"
}

const toneIcon: Record<Tone, IconName> = {
	ok: "lucide:circle-check",
	warn: "lucide:loader-circle",
	bad: "lucide:circle-x",
	muted: "lucide:circle-dashed"
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

function toneOf(status: ShellStatus): Tone {
	return status.attachment === "absent" || status.attachment === "exited"
		? "bad"
		: status.sweeping ? "warn" : phaseTone[status.phase]
}

/** The status window's page: the core, the shell, and the watcher. */
@component("p7t-status")
export class Status extends Component {
	@state() private status?: ShellStatus
	private unsubscribe?: () => void

	static override get styles() {
		return css`
			:host {
				display: block;
				padding: 20px 22px 24px;
				color: var(--text-normal);
				font-family: var(--font-interface);
			}

			header {
				display: flex;
				align-items: center;
				flex-wrap: wrap;
				gap: .4em 1em;
				margin: 0 .3em 1em;
			}

			h1 {
				margin: 0;
				font-size: 1.45em;
				font-weight: 600;
				letter-spacing: .08em;
			}

			.chips {
				display: flex;
				flex-wrap: wrap;
				align-items: center;
				gap: .4em .9em;
				font-size: .9em;
			}

			.tone-ok, p7t-card[data-tone=ok]::part(heading) { color: var(--color-green); }
			.tone-warn, p7t-card[data-tone=warn]::part(heading) { color: var(--color-yellow); }
			.tone-bad, p7t-card[data-tone=bad]::part(heading) { color: var(--color-red); }
			.tone-muted, p7t-card[data-tone=muted]::part(heading) { color: var(--text-muted); }

			.facts {
				display: grid;
				grid-template-columns: max-content minmax(0, 1fr);
				align-items: center;
				gap: .55em 1.2em;
				padding-inline: .5em;
			}

			.facts dt {
				color: var(--text-muted);
				font-size: .8em;
				text-transform: uppercase;
				letter-spacing: .06em;
			}

			.facts dd {
				margin: 0;
				min-width: 0;
			}

			.facts p7t-icon-item {
				overflow-wrap: anywhere;
			}

			.message {
				white-space: pre-wrap;
				padding-inline: .5em;
				margin: 0 0 .9em;
				color: var(--text-muted);
			}

			.action-row {
				font-size: .8em;
				display: flex;
				gap: .4em;
				color: var(--text-accent);
			}

			.actions {
				display: flex;
				flex-wrap: wrap;
				gap: .5em;
				padding: .9em .5em .1em;
			}

			label.toggle {
				display: inline-flex;
				align-items: center;
				gap: .6em;
				padding-inline: .5em;
				cursor: pointer;
			}

			label.toggle input {
				accent-color: var(--interactive-accent);
				width: 1.1em;
				height: 1.1em;
				margin: 0;
			}
		`
	}

	override connectedCallback() {
		super.connectedCallback()
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
		const status = this.status
		if (!status) {
			return html``
		}

		const tone = toneOf(status)
		return html`
			<header>
				<h1>PLAINTORCH</h1>
				<div class="chips">
					<p7t-icon-item class="tone-${tone}" small icon=${toneIcon[tone]} text=${phaseLabel(status)}></p7t-icon-item>
					<p7t-icon-item small icon="lucide:package" text="${status.flavor} · v${status.version}"></p7t-icon-item>
				</div>
			</header>

			${this.coreCard(status, tone)}
			${this.shellCard(status)}
			<p7t-watcher-status-card></p7t-watcher-status-card>
		`
	}

	private coreCard(status: ShellStatus, tone: Tone) {
		return html`
			<p7t-card data-tone=${tone} .preHeading=${"Core"} .heading=${phaseLabel(status)} .subHeading=${this.describeAttachment(status)}>
				${status.message ? html`<p class="message">${status.message}</p>` : nothing}
				<dl class="facts">
					<dt>Vault</dt>
					<dd>
						<p7t-icon-item small icon="lucide:folder-open" text=${status.vault ?? status.activeVaultSetting ?? "none"}></p7t-icon-item>
						<span class='action-row'>
							<p7t-button ghost emphasis icon="lucide:folder-open" @click=${() => bridge.activateVault()}>Activate vault…</p7t-button>
							<p7t-button ghost danger icon="lucide:folder-x" ?disabled=${!status.activeVaultSetting} @click=${() => bridge.deactivateVault()}>Deactivate vault</p7t-button>
						</span>
					</dd>
					<dt>Endpoint</dt>
					<dd><p7t-icon-item small icon="lucide:plug" text=${status.endpoint}></p7t-icon-item></dd>
					<dt>Profile</dt>
					<dd><p7t-icon-item small icon="lucide:user-round" text=${status.profile}></p7t-icon-item></dd>
					${status.pid ? html`
						<dt>Process</dt>
						<dd><p7t-icon-item small icon="lucide:cpu" text=${String(status.pid)}></p7t-icon-item></dd>
					` : nothing}
				</dl>
				<div class="actions" slot="footer">
					<p7t-button emphasis icon="lucide:layout-dashboard" @click=${() => bridge.openBriefing()}>Open briefing</p7t-button>
					${status.flavor === "standalone" ? html`<p7t-button icon="lucide:rotate-cw" @click=${() => bridge.restartCore()}>Restart core</p7t-button>` : nothing}
					<p7t-button icon="lucide:scroll-text" @click=${() => bridge.openLogs()}>Open logs</p7t-button>
				</div>
			</p7t-card>
		`
	}

	private shellCard(status: ShellStatus) {
		return html`
			<p7t-card .preHeading=${"Shell"} .heading=${status.flavor === "standalone" ? "Standalone" : "Client"} .subHeading=${`Version ${status.version}`}>
				<label class="toggle">
					<input type="checkbox" .checked=${status.autostart} @change=${(event: Event) => bridge.setAutostart((event.target as HTMLInputElement).checked)}>
					Start at login
				</label>
				<div class="actions" slot="footer">
					${this.updateTemplate(status)}
					<p7t-button danger icon="lucide:power" @click=${() => bridge.quit()}>Quit PLAINTORCH</p7t-button>
				</div>
			</p7t-card>
		`
	}

	private describeAttachment(status: ShellStatus): string {
		switch (status.attachment) {
			case "spawned": return "Hosted by this app"
			case "attached": return "Attached to an external core"
			case "absent": return "No core reachable"
			case "exited": return `Exited${status.exitCode !== undefined ? ` (code ${status.exitCode})` : ""}`
			default: return "Starting"
		}
	}

	private updateTemplate(status: ShellStatus) {
		const update = status.updateState
		switch (update.kind) {
			case "available":
				return html`<p7t-button emphasis icon="lucide:download" @click=${() => bridge.installUpdate()}>Download update ${update.version}</p7t-button>`
			case "ready":
				return html`<p7t-button emphasis icon="lucide:download" @click=${() => bridge.installUpdate()}>Install ${update.version} and restart</p7t-button>`
			case "downloading":
				return html`<p7t-button disabled icon="lucide:loader-circle">Downloading… ${update.percent}%</p7t-button>`
			case "checking":
				return html`<p7t-button disabled icon="lucide:loader-circle">Checking…</p7t-button>`
			case "unavailable":
				return html`<p7t-button disabled icon="lucide:refresh-cw" label=${update.reason}>Check for updates</p7t-button>`
			case "error":
				return html`<p7t-button icon="lucide:refresh-cw" label=${update.message} @click=${() => bridge.checkForUpdates()}>Retry update check</p7t-button>`
			case "none":
				return html`<p7t-button icon="lucide:circle-check" @click=${() => bridge.checkForUpdates()}>Up to date (${update.version})</p7t-button>`
			default:
				return html`<p7t-button icon="lucide:refresh-cw" @click=${() => bridge.checkForUpdates()}>Check for updates</p7t-button>`
		}
	}
}
