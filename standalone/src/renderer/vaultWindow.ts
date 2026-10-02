import { css, html, render } from "@a11d/lit"
import { PlaintorchCoreClient } from "@pleiades/sdk/plaintorch"
import { adoptStyles, provideCore, provideHost } from "@pleiades/sipa"
import { createSipaHost } from "@pleiades/sipa/hosts/sipa"
import { mediaScheme, type PlaintorchBridge, type ShellStatus } from "../shared/contracts"
import { BridgeTransport } from "./bridgeTransport"
import "./WindowFrame"

declare global {
	interface Window {
		plaintorch: PlaintorchBridge
	}
}

export interface VaultWindowOptions {
	/** Where the page draws: its content once the vault can be shown, the wait for it until then. */
	readonly container: HTMLElement
	/** What the window shows, as the waiting messages name it: "the briefing", "the planning graph". */
	readonly subject: string
	/** The page's content, drawn once the vault can be shown and again on every {@link VaultWindow.repaint}. */
	readonly content: () => unknown
}

export interface VaultWindow {
	readonly bridge: PlaintorchBridge
	/** Draws the page again for the last status, for content that changed. */
	repaint(): void
}

/** The wait's layout: the PLAINTORCH glyph, the message, and its actions, centred in the window. */
const waitingStyles = css`
	.waiting {
		display: grid;
		place-content: center;
		justify-items: center;
		gap: 14px;
		height: 100%;
		text-align: center;
	}

	.waiting .glyph {
		width: 72px;
		height: 72px;
		color: var(--p7t-accent-soft);
	}

	.waiting .message {
		max-width: 440px;
		color: var(--text-muted);
		white-space: pre-wrap;
	}

	.waiting .actions {
		display: flex;
		gap: 8px;
	}
`

/**
 * Mounts a window that shows the vault — the briefing, a planning window — hosted by the shell under the window's own
 * title bar (`p7t-window-frame`, in the page).
 *
 * Bootstrap order matters. The host and the core client are installed before anything renders: the components reach
 * for the host while they draw and take their repositories from the client as they are built. The change feed then
 * keeps whatever is on screen current as the vault changes elsewhere (Obsidian, the watcher, the CLI).
 *
 * The content waits for a core serving an active vault whose startup sweep is done; until then the page says what it is
 * waiting for, and offers to activate a vault when none is.
 */
export function mountVaultWindow(options: VaultWindowOptions): VaultWindow {
	const bridge = window.plaintorch

	/** The vault the core serves, for media URLs: the shell serves its files under the media scheme. */
	let servedVault: string | undefined

	provideHost(createSipaHost({
		mediaScheme,
		mediaUrl: vaultRelativePath => servedVault === undefined
			? undefined
			// The vault in the query keeps an image of one vault from being served from the cache for another.
			: `${mediaScheme}://vault/${vaultRelativePath.split("/").map(encodeURIComponent).join("/")}?vault=${encodeURIComponent(servedVault)}`,
		// A scratch global context is saved through the shell's save dialog, anywhere, and opens in a planning window.
		globalContexts: {
			saveAndOpen: async text => {
				const outcome = await bridge.planning.saveAs(text)
				if (outcome && "error" in outcome) {
					throw new Error(outcome.error)
				}

				return outcome
			}
		},
		// A canvas context pops out into a planning window, as a planning graph file opens.
		canvasPopOut: {
			label: "Open in a new window",
			popOut: context => bridge.planning.popOut(context.mode === "global" ? { mode: "global", text: context.text } : { mode: context.mode })
		}
	}))
	adoptStyles(document, waitingStyles)

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

	const waitingMessage = (status: ShellStatus): string => {
		if (status.attachment === "absent" || status.attachment === "exited") {
			return status.message ?? "No PLAINTORCH core is running."
		}

		if (status.sweeping) {
			return `Running the startup sweep… ${options.subject} appears once it finishes.`
		}

		if (status.phase === "Idle") {
			return `No vault is active. Activate one to see ${options.subject}.`
		}

		return status.message ?? "Getting the vault ready…"
	}

	const waitingTemplate = (status: ShellStatus) => {
		const idle = status.phase === "Idle"
		return html`
			<div class='waiting'>
				<p7t-icon class='glyph' icon='plaintorch'></p7t-icon>
				<div class='message'>${waitingMessage(status)}</div>
				<div class='actions'>
					${idle ? html`<p7t-button large emphasis class='mod-cta' @click=${() => bridge.activateVault()}>Activate vault…</p7t-button>` : ""}
					<p7t-button large @click=${() => bridge.openStatus()}>Open status</p7t-button>
				</div>
			</div>
		`
	}

	let lastStatus: ShellStatus | undefined
	let lastVaultKey: string | undefined

	const draw = () => {
		if (lastStatus) {
			render(isReady(lastStatus) ? options.content() : waitingTemplate(lastStatus), options.container)
		}
	}

	const paint = (status: ShellStatus) => {
		lastStatus = status
		servedVault = status.vault
		// A different vault, or one coming back from a restart, invalidates everything held for the previous one; the
		// feed would do the same on reconnecting, but a core that kept its feed open across a vault switch never does.
		const vaultKey = `${status.vault}|${status.phase}|${status.sweeping}`
		if (lastVaultKey !== undefined && vaultKey !== lastVaultKey && isReady(status)) {
			core.repos.invalidateAll()
			void core.repos.revalidateObserved()
		}

		lastVaultKey = vaultKey
		draw()
	}

	bridge.onStatus(paint)
	void bridge.getStatus().then(paint)
	return { bridge, repaint: draw }
}

/** Whether the vault can be shown: a core serving an active vault whose startup sweep is done. */
function isReady(status: ShellStatus): boolean {
	return (status.attachment === "spawned" || status.attachment === "attached") && status.phase === "Active" && !status.sweeping
}
