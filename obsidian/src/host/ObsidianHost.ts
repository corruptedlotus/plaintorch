import { getIcon, getIconIds, normalizePath, Notice, type App, type TFile } from "obsidian"
import type { GlobalContextHost, IconHost, MediaHost, NavigationHost, OpenNoteOptions, PlatformHost } from "@pleiades/sipa"

/** How often, and for how long, a note the core has just written is looked for before it is opened (PEP110). */
const noteReadyPollIntervalMs = 120
const noteReadyPollTimeoutMs = 6_000

/** The `lucide-` prefix Obsidian registers its bundled lucide icons under (as `getIconIds` returns them). */
const lucideIdPrefix = "lucide-"

/**
 * The Obsidian implementation of the SIPA {@link PlatformHost}: toasts are Notices, notes open in workspace tabs,
 * vault files load through Obsidian's resource URLs, and the lucide glyphs come from Obsidian's own icon registry.
 */
export class ObsidianHost implements PlatformHost {
	readonly name = "obsidian"

	constructor(private readonly app: App) { }

	toast(message: string): void {
		new Notice(message)
	}

	readonly navigation: NavigationHost = {
		openNote: async (vaultRelativePath: string, options?: OpenNoteOptions) => {
			if (!options?.waitForFile) {
				await this.app.workspace.openLinkText(vaultRelativePath, "", true)
				return
			}

			// Even a ready write is indexed a beat after it lands, so the file is looked for rather than assumed:
			// assuming it made a rename-reveal throw and a create-then-open spawn a phantom note.
			let file = this.app.vault.getFileByPath(vaultRelativePath)
			if (!file) {
				if (options.pending) {
					new Notice("The note will be available shortly…")
				}

				file = await this.waitForVaultFile(vaultRelativePath)
			}

			if (file) {
				await this.app.workspace.getLeaf(true).openFile(file)
			}
		},
		followMovedNote: async (vaultRelativePath: string, pending: boolean) => {
			if (this.app.workspace.activeEditor?.file?.path === vaultRelativePath) {
				return
			}

			await this.navigation.openNote(vaultRelativePath, { waitForFile: true, pending })
		},
	}

	readonly media: MediaHost = {
		resourceUrl: (vaultRelativePath: string) => {
			const file = this.app.vault.getFileByPath(vaultRelativePath)
			return file ? this.app.vault.getResourcePath(file) : undefined
		},
		isResourceUrl: (value: string) => /^app:/i.test(value),
	}

	readonly icons: IconHost = {
		lucide: (name: string) => getIcon(name.replace(/^lucide[:-]/, "")) ?? undefined,
		lucideNames: () => getIconIds()
			.filter(id => id.startsWith(lucideIdPrefix))
			.map(id => id.slice(lucideIdPrefix.length)),
	}

	readonly globalContexts: GlobalContextHost = {
		createAndOpen: async (fileName: string, text: string) => {
			const path = normalizePath(fileName)
			if (this.app.vault.getAbstractFileByPath(path)) {
				return { created: false, path }
			}

			const file = await this.app.vault.create(path, text)
			await this.app.workspace.getLeaf(true).openFile(file)
			return { created: true, path }
		},
	}

	/** Polls the vault until a path resolves to a file or the wait elapses — the file lands a beat after the core writes it. */
	private async waitForVaultFile(path: string): Promise<TFile | null> {
		const deadline = Date.now() + noteReadyPollTimeoutMs
		for (;;) {
			const file = this.app.vault.getFileByPath(path)
			if (file) {
				return file
			}

			if (Date.now() >= deadline) {
				return null
			}

			await new Promise(resolve => setTimeout(resolve, noteReadyPollIntervalMs))
		}
	}
}
