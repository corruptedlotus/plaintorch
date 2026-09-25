import { Component, component, css, html, nothing, state, unsafeCSS } from "@a11d/lit"
import type { PlaintorchBridge, ShellStatus } from "../shared/contracts"
import splashArtwork from "../../assets/splash-loading.png"

declare global {
	interface Window {
		plaintorch: PlaintorchBridge
	}
}

const bridge = window.plaintorch

/**
 * The frameless startup popup: the artwork with the core's latest phase line over it. Its page is its own and small —
 * no SIPA — so it shows the moment the app starts.
 */
@component("p7t-splash")
export class Splash extends Component {
	@state() private status?: ShellStatus
	private unsubscribe?: () => void

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
		if (!this.status) {
			return html``
		}

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
