import { addIcon, Notice, Plugin, type WorkspaceLeaf } from "obsidian"
import { PlaintorchBriefingView, PLAINTORCH_BRIEFING_VIEW_TYPE } from "./briefing/PlaintorchBriefingView"
import { PlaintorchCanvasView, PLAINTORCH_CANVAS_VIEW_TYPE } from "./canvas/PlaintorchCanvasView"
import { PageBannerRenderer } from "./banner/PageBannerRenderer"

import 'components'

type PlaintorchNodeCoreClient = typeof import("@pleiades/sdk/plaintorch/node").plaintorchNodeCoreClient

let cachedCoreClient: PlaintorchNodeCoreClient | undefined

export default class PlaintorchObsidianPlugin extends Plugin {
	public override async onload(): Promise<void> {

		addIcon("plaintorch", `
			<g id="PLAINTORCH-Dark-2" data-name="PLAINTORCH-Dark">
				<defs>
					<style>
						.xtroke {
							fill: none;
							stroke: currentColor;
							stroke-linecap: round;
							stroke-linejoin: round;
							stroke-width: calc(100 * var(--icon-stroke) / 24);
						}
					</style>
				</defs>
				<g id="plaintorch-mono">
					<path d="M45.71,94.42c1-5.63,4.78-10.35,8.81-14.4S63,72.22,66,67.33,70.18,56,67.27,51.06" class='xtroke' />
					<path d="M56.77,78c4.86-3.24,9.75-6.56,13.72-10.85s7-9.7,7.36-15.53-2.29-12-7.39-14.89S58,35.33,55.12,40.43" class='xtroke' />
					<line x1="27.97" y1="72.03" x2="20.02" y2="79.98" class='xtroke' />
					<line x1="27.97" y1="27.97" x2="20.02" y2="20.02" class='xtroke' />
					<line x1="77.53" y1="77.53" x2="79.98" y2="79.98" class="xtroke" />
					<line x1="72.03" y1="27.97" x2="79.98" y2="20.02" class="xtroke" />
					<path d="M75.76,24.24A36.43,36.43,0,1,0,34.49,83" class="xtroke"
						style="stroke-dasharray:28.269662857055664,8.404494285583496,0,8.404494285583496" />
					<path
						d="M72.54,49.17,60,43.52A7.1,7.1,0,0,1,56.48,40l-5.65-12.5a.91.91,0,0,0-1.66,0L43.52,40A7.1,7.1,0,0,1,40,43.52l-12.5,5.65a.91.91,0,0,0,0,1.66L40,56.48A7.1,7.1,0,0,1,43.52,60l5.65,12.5a.91.91,0,0,0,1.66,0L56.48,60A7.1,7.1,0,0,1,60,56.48l12.5-5.65A.91.91,0,0,0,72.54,49.17ZM57.08,50.29l-3.9,1.76a2.3,2.3,0,0,0-1.13,1.13l-1.76,3.9a.32.32,0,0,1-.58,0L48,53.18a2.3,2.3,0,0,0-1.13-1.13l-3.9-1.76a.32.32,0,0,1,0-.58L46.82,48A2.3,2.3,0,0,0,48,46.82l1.76-3.9a.32.32,0,0,1,.58,0l1.76,3.9A2.3,2.3,0,0,0,53.18,48l3.9,1.76A.32.32,0,0,1,57.08,50.29Z"
						style="fill: currentColor" />
				</g>
			</g>
		`)

		const renderer = new PageBannerRenderer(this.app)
		this.registerEditorExtension(renderer.createEditorExtension())
		this.registerMarkdownPostProcessor(renderer.readingModeRenderer)

		this.registerView(
			PLAINTORCH_BRIEFING_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchBriefingView(leaf)
		)

		this.registerView(
			PLAINTORCH_CANVAS_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchCanvasView(leaf)
		)

		this.addRibbonIcon("plaintorch", "PLAINTORCH briefing", () => {
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
			id: "open-plaintorch-dependency-canvas",
			name: "Open PLAINTORCH dependency canvas",
			callback: () => {
				void this.activateCanvasView()
			}
		})

		this.addCommand({
			id: "init-directive-from-current-file",
			name: "Initialize directive from current file",
			callback: () => {
				void this.initializeDirectiveFromCurrentFile()
			}
		})

		void this.startChangeFeed()
	}

	public override onunload(): void {
		cachedCoreClient?.repos.changeFeed.stop()
		this.app.workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE)
		this.app.workspace.detachLeavesOfType(PLAINTORCH_CANVAS_VIEW_TYPE)
	}

	/**
	 * Listens for writes made outside the plugin, so a markdown edit the watcher picks up or a change made
	 * by the CLI reaches whatever is on screen.
	 *
	 * The feed is an optimization, never a requirement — revalidating on leaf activation covers the case
	 * where it cannot be established at all, and is what the plugin relied on before it existed.
	 */
	private async startChangeFeed(): Promise<void> {
		const coreClient = await getPlaintorchNodeCoreClient()
		coreClient.repos.changeFeed.start()

		this.registerEvent(this.app.workspace.on("active-leaf-change", () => {
			if (!coreClient.repos.changeFeed.connected) {
				void coreClient.repos.revalidateObserved()
			}
		}))
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

	/**
	 * Opens the dependency canvas in its own leaf, reusing the one already open rather than replacing it.
	 *
	 * Unlike the briefing, this is a surface to keep beside whatever is being planned, so an existing canvas
	 * is revealed instead of detached and rebuilt — which would throw away its pan, zoom and arrangement.
	 */
	private async activateCanvasView(): Promise<void> {
		const workspace = this.app.workspace
		const [existing] = workspace.getLeavesOfType(PLAINTORCH_CANVAS_VIEW_TYPE)
		const leaf = existing ?? workspace.getLeaf(true)

		if (!existing) {
			await leaf.setViewState({
				type: PLAINTORCH_CANVAS_VIEW_TYPE,
				active: true
			})
		}

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
