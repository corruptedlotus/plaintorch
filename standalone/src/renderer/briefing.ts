import { html, render } from "@a11d/lit"
import { PlaintorchCoreClient } from "@pleiades/sdk/plaintorch"
import { provideCore, provideHost } from "@pleiades/sipa"
import { createSipaHost } from "@pleiades/sipa/hosts/sipa"
import { mediaScheme, type PlaintorchBridge, type ShellStatus } from "../shared/contracts"
import { BridgeTransport } from "./bridgeTransport"

/**
 * The briefing window: the SIPA UI — the same `p7t-briefing` the Obsidian plugin shows — hosted by the shell.
 *
 * Bootstrap order matters. The host and the core client are installed before anything renders: the components reach
 * for the host while they draw and take their repositories from the client as they are built. The change feed then
 * keeps whatever is on screen current as the vault changes elsewhere (Obsidian, the watcher, the CLI).
 */

declare global {
	interface Window {
		plaintorch: PlaintorchBridge
	}
}

const bridge = window.plaintorch

/** The vault the core serves, for media URLs: the shell serves its files under the media scheme. */
let servedVault: string | undefined

provideHost(createSipaHost({
	mediaScheme,
	mediaUrl: vaultRelativePath => servedVault === undefined
		? undefined
		// The vault in the query keeps an image of one vault from being served from the cache for another.
		: `${mediaScheme}://vault/${vaultRelativePath.split("/").map(encodeURIComponent).join("/")}?vault=${encodeURIComponent(servedVault)}`
}))

const core = new PlaintorchCoreClient({ transports: [new BridgeTransport(bridge)] })
provideCore(core)
core.repos.changeFeed.start()
core.repos.startEvictionSweep()

// Without a live feed nothing polls, so a returning window refreshes what it shows.
const revalidateUnlessLive = () => {
	if (!core.repos.changeFeed.connected) {
		void core.repos.revalidateObserved()
	}
}
window.addEventListener("focus", revalidateUnlessLive)
document.addEventListener("visibilitychange", () => document.visibilityState === "visible" && revalidateUnlessLive())
window.addEventListener("pagehide", () => {
	core.repos.changeFeed.stop()
	core.repos.stopEvictionSweep()
})

/** Whether the vault can be shown: a core serving an active vault whose startup sweep is done. */
function isReady(status: ShellStatus): boolean {
	return (status.attachment === "spawned" || status.attachment === "attached") && status.phase === "Active" && !status.sweeping
}

function waitingMessage(status: ShellStatus): string {
	if (status.attachment === "absent" || status.attachment === "exited") {
		return status.message ?? "No PLAINTORCH core is running."
	}

	if (status.sweeping) {
		return "Running the startup sweep… the briefing appears once it finishes."
	}

	if (status.phase === "Idle") {
		return "No vault is active. Activate one to see its briefing."
	}

	return status.message ?? "Getting the vault ready…"
}

function waitingTemplate(status: ShellStatus) {
	const idle = status.phase === "Idle"
	return html`
		<div class='waiting'>
			<p7t-icon class='glyph' icon='plaintorch'></p7t-icon>
			<div class='message'>${waitingMessage(status)}</div>
			<div class='actions'>
				${idle ? html`<button class='mod-cta' @click=${() => bridge.activateVault()}>Activate vault…</button>` : ""}
				<button @click=${() => bridge.openStatus()}>Open status</button>
			</div>
		</div>
	`
}

const container = document.getElementById("briefing")!
let lastVaultKey: string | undefined

function paint(status: ShellStatus) {
	servedVault = status.vault
	// A different vault, or one coming back from a restart, invalidates everything held for the previous one; the
	// feed would do the same on reconnecting, but a core that kept its feed open across a vault switch never does.
	const vaultKey = `${status.vault}|${status.phase}|${status.sweeping}`
	if (lastVaultKey !== undefined && vaultKey !== lastVaultKey && isReady(status)) {
		core.repos.invalidateAll()
		void core.repos.revalidateObserved()
	}

	lastVaultKey = vaultKey
	render(isReady(status) ? html`<p7t-briefing></p7t-briefing>` : waitingTemplate(status), container)
}

bridge.onStatus(paint)
void bridge.getStatus().then(paint)
