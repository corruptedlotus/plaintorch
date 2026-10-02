import { html } from "@a11d/lit"
import { mountVaultWindow } from "./vaultWindow"

/**
 * The briefing window: the SIPA UI — the same `p7t-briefing` the Obsidian plugin shows — hosted by the shell, under
 * the window's own title bar. {@link mountVaultWindow} installs the host and the core client and holds the briefing
 * back until the vault can be shown.
 */
mountVaultWindow({
	container: document.getElementById("briefing")!,
	subject: "the briefing",
	content: () => html`<p7t-briefing></p7t-briefing>`
})
