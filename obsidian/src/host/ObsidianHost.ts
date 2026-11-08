import { getIcon, getIconIds, normalizePath, Notice, type App, type TFile, type WorkspaceLeaf } from "obsidian"
import { GLOBAL_CONTEXT_EXTENSION, PromptTextModal } from "@pleiades/sipa"
import type {
	CanvasPopOut,
	CanvasPopOutHost,
	DialogHost,
	GlobalContextHost,
	IconHost,
	MediaHost,
	NavigationHost,
	OpenNoteOptions,
	PlatformHost
} from "@pleiades/sipa"
import { PLAINTORCH_PLANNING_VIEW_TYPE, PlaintorchPlanningView, type PlanningViewState } from "../canvas/PlaintorchPlanningView"
import { createObsidianDialogs } from "./obsidianDialogs"

/** How often, and for how long, a note the core has just written is looked for before it is opened (PEP110). */
const noteReadyPollIntervalMs = 120
const noteReadyPollTimeoutMs = 6_000

/** The `lucide-` prefix Obsidian registers its bundled lucide icons under (as `getIconIds` returns them). */
const lucideIdPrefix = "lucide-"

/**
 * The Obsidian implementation of the SIPA {@link PlatformHost}: toasts are Notices, dialogs are Obsidian modals, notes
 * open in workspace tabs, vault files load through Obsidian's resource URLs, and the lucide glyphs come from
 * Obsidian's own icon registry. Global contexts save to the vault root, and canvas contexts pop out into tabs.
 */
export class ObsidianHost implements PlatformHost {
	readonly name = "obsidian"
	readonly dialogs: DialogHost

	constructor(private readonly app: App) {
		this.dialogs = createObsidianDialogs(app)
	}

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

	/**
	 * A global context is saved under a name asked for, at the vault root — Obsidian opens only the vault's own
	 * files — and opens in a new tab, or, saved from a popped-out tab holding it unsaved, in that same tab, which
	 * becomes the file's view. A dismissed prompt saves nothing; a name already taken is refused rather than
	 * overwritten.
	 */
	readonly globalContexts: GlobalContextHost = {
		saveAndOpen: async (text: string, origin?: HTMLElement) => {
			let name: string | undefined
			try {
				name = await PromptTextModal.prompt("Save global context", "File name", "", "Save")
			}
			catch {
				return undefined
			}

			if (!name) {
				return undefined
			}

			const path = normalizePath(`${name.replace(/[\\/:*?"<>|]/g, "").trim()}.${GLOBAL_CONTEXT_EXTENSION}`)
			if (this.app.vault.getAbstractFileByPath(path)) {
				new Notice(`A file named "${path}" already exists.`)
				return undefined
			}

			const file = await this.app.vault.create(path, text)
			const unsaved = origin && this.leafOf(origin)
			const leaf = unsaved?.view instanceof PlaintorchPlanningView && unsaved.view.isUnsaved ? unsaved : this.app.workspace.getLeaf(true)
			await leaf.openFile(file)
			return { path }
		},
	}

	/**
	 * A canvas context pops out into a tab of its own, as a planning graph file opens: an onrush has one tab, brought
	 * forward when it is open; a global context opens a new tab every time, holding it unsaved.
	 */
	readonly canvasPopOut: CanvasPopOutHost = {
		label: "Open in a new tab",
		popOut: async (context: CanvasPopOut) => {
			const workspace = this.app.workspace
			if (context.mode !== "global") {
				// Matched on the saved view state, which a tab Obsidian has not loaded yet (a deferred one) has too.
				const open = workspace.getLeavesOfType(PLAINTORCH_PLANNING_VIEW_TYPE).find(leaf => leaf.getViewState().state?.["mode"] === context.mode)
				if (open) {
					await workspace.revealLeaf(open)
					return
				}
			}

			const state: PlanningViewState = context.mode === "global" ? { mode: "global", text: context.text } : { mode: context.mode }
			const leaf = workspace.getLeaf("tab")
			await leaf.setViewState({ type: PLAINTORCH_PLANNING_VIEW_TYPE, active: true, state: { ...state } })
			await workspace.revealLeaf(leaf)
		},
	}

	/** The workspace leaf whose view holds an element, if any does. */
	private leafOf(element: HTMLElement): WorkspaceLeaf | undefined {
		let found: WorkspaceLeaf | undefined
		this.app.workspace.iterateAllLeaves(leaf => {
			if (!found && leaf.view.containerEl.contains(element)) {
				found = leaf
			}
		})
		return found
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
