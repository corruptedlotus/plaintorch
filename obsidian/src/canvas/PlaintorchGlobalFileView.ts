import { TextFileView, type WorkspaceLeaf } from "obsidian"
import type { DependencyCanvas, GlobalContextSnapshot } from "@pleiades/sipa"
import { parseGlobalContext, serializeGlobalContext } from "@pleiades/sipa"

export const PLAINTORCH_GLOBAL_VIEW_TYPE = "plaintorch-global-context"

/**
 * The leaf a saved global planning context (`.p7tpx`) opens into.
 *
 * A global context has no database home, so the file *is* the context: this view reads the pinned set and the
 * layout out of the file into the canvas, and writes them back as the canvas is edited. It hosts the same
 * `p7t-dependency-canvas` the onrush tabs do, locked to its one global context — no mode switcher — via the
 * canvas's `fileBacked` flag.
 */
export class PlaintorchGlobalFileView extends TextFileView {
	private canvas?: DependencyCanvas
	private snapshot: GlobalContextSnapshot = { pinned: [], layout: undefined }

	public constructor(leaf: WorkspaceLeaf) {
		super(leaf)
	}

	public override getViewType(): string {
		return PLAINTORCH_GLOBAL_VIEW_TYPE
	}

	public override getIcon(): string {
		return "git-fork"
	}

	/** The text Obsidian writes to the file: the current pinned set and arrangement. */
	public override getViewData(): string {
		return serializeGlobalContext(this.snapshot.pinned, this.snapshot.layout)
	}

	/** Loads the file's context into the canvas. Called by Obsidian when the file opens or changes on disk. */
	public override setViewData(data: string, _clear: boolean): void {
		const context = parseGlobalContext(data)
		this.snapshot = { pinned: context.pinned, layout: context.layout }
		const canvas = this.ensureCanvas()
		canvas.pinned = context.pinned
		canvas.savedLayout = context.layout
	}

	public override clear(): void {
		this.snapshot = { pinned: [], layout: undefined }
		if (this.canvas) {
			this.canvas.pinned = []
			this.canvas.savedLayout = undefined
		}
	}

	public override async onClose(): Promise<void> {
		this.canvas = undefined
		this.contentEl.empty()
	}

	/**
	 * Creates the canvas once and wires it as a global, file-backed context.
	 *
	 * The `contextChanged` event carries the whole snapshot on every pin or drag; capturing it and asking
	 * Obsidian to save is what keeps the file in step with the view. `requestSave` is debounced by Obsidian,
	 * so a run of edits collapses into one write.
	 */
	private ensureCanvas(): DependencyCanvas {
		if (this.canvas) {
			return this.canvas
		}

		this.contentEl.empty()
		this.contentEl.addClass("plaintorch-root")
		this.contentEl.addClass("plaintorch-canvas-view")
		const canvas = this.contentEl.createEl("p7t-dependency-canvas") as DependencyCanvas
		canvas.mode = "global"
		canvas.fileBacked = true
		canvas.addEventListener("contextChanged", (event: Event) => {
			this.snapshot = (event as CustomEvent<GlobalContextSnapshot>).detail
			this.requestSave()
		})

		this.canvas = canvas
		return canvas
	}
}
