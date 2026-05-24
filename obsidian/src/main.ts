import { Notice, Plugin, type WorkspaceLeaf } from "obsidian"
import { PlaintorchBriefingView, PLAINTORCH_BRIEFING_VIEW_TYPE } from "./briefing/PlaintorchBriefingView"
import { PageBannerRenderer } from "./banner/PageBannerRenderer"

import 'components'

type PlaintorchNodeCoreClient = typeof import("@pleiades/sdk/plaintorch/node").plaintorchNodeCoreClient

let cachedCoreClient: PlaintorchNodeCoreClient | undefined

export default class PlaintorchObsidianPlugin extends Plugin {
	public override async onload(): Promise<void> {

		const renderer = new PageBannerRenderer(this.app)
		this.registerEditorExtension(renderer.createEditorExtension())
		this.registerMarkdownPostProcessor(renderer.readingModeRenderer)

		this.registerView(
			PLAINTORCH_BRIEFING_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchBriefingView(leaf)
		)

		this.addRibbonIcon("sparkles", "PLAINTORCH briefing", () => {
			void this.activateBriefingView()
		})

		this.addCommand({
			id: "open-plaintorch-briefing",
			name: "Open PLAINTORCH briefing",
			callback: () => {
				void this.activateBriefingView()
			}
		})

		this.addCommand({
			id: "init-directive-from-current-file",
			name: "Initialize directive from current file",
			callback: () => {
				void this.initializeDirectiveFromCurrentFile()
			}
		})
	}

	public override onunload(): void {
		this.app.workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE)
	}

	private async activateBriefingView(): Promise<void> {
		const workspace = this.app.workspace
		workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE)
		const leaf = workspace.getLeaf(true)

		await leaf.setViewState({
			type: PLAINTORCH_BRIEFING_VIEW_TYPE,
			active: true
		})

		workspace.revealLeaf(leaf)
	}

	private async initializeDirectiveFromCurrentFile(): Promise<void> {
		const activeFile = this.app.workspace.getActiveFile()
		if (!activeFile || activeFile.extension.toLowerCase() !== "md") {
			new Notice("Open a markdown file to initialize a directive")
			return
		}

		try {
			const coreClient = await getPlaintorchNodeCoreClient()
			const initialized = await coreClient.directives.init({ path: activeFile.path })
			if (!initialized) {
				new Notice("Directive initialization did not return an entity")
				return
			}

			new Notice(`Directive initialized: ${initialized.id}`)
		}
		catch (error) {
			console.error("Failed to initialize directive from current file", error)
			new Notice(`Directive initialization failed: ${describeError(error)}`)
		}
	}
}

async function getPlaintorchNodeCoreClient(): Promise<PlaintorchNodeCoreClient> {
	if (cachedCoreClient) {
		return cachedCoreClient
	}

	const module = await import("@pleiades/sdk/plaintorch/node")
	cachedCoreClient = module.plaintorchNodeCoreClient
	return cachedCoreClient
}

function describeError(error: unknown): string {
	if (error instanceof Error && error.message) {
		return error.message
	}

	return "unknown error"
}
