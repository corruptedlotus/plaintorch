import { ItemView, type ViewStateResult, type WorkspaceLeaf } from "obsidian"
import type { CanvasContextMode, DependencyCanvas, GlobalContextSnapshot } from "@pleiades/sipa"
import { contextModeLabels, parseGlobalContext, serializeGlobalContext } from "@pleiades/sipa"

export const PLAINTORCH_PLANNING_VIEW_TYPE = "plaintorch-planning"

/** What a planning tab shows, as the workspace keeps it: a context mode, and an unsaved global context's file text. */
export interface PlanningViewState {
	mode: CanvasContextMode
	/** The unsaved global context, as the text its `.p7tpx` file would hold; global mode only. */
	text?: string
}

/**
 * A dependency canvas context popped out into a tab of its own — the canvas's "Open in a new tab" — as a planning
 * graph file opens into its view. It shows one context alone, with no mode switcher and no pop-out:
 *
 * - **an onrush**, the active or the planning one. The vault holds it, so the tab it was popped out of stays current
 *   too, and there is one tab per onrush (the host brings it forward).
 * - **a global context not saved to a file yet**, moved here from the canvas it was popped out of. The tab is marked
 *   unsaved — in its title and at the start of the canvas's toolbar — and keeps the context in its view state, which
 *   the workspace saves, so it survives a reload. Its "Save to file" opens the new file in this same tab, which
 *   becomes the file's view.
 */
export class PlaintorchPlanningView extends ItemView {
	private state: PlanningViewState = { mode: "onrush-active" }

	public constructor(leaf: WorkspaceLeaf) {
		super(leaf)
	}

	/** Whether the tab holds a global context no file has yet. */
	public get isUnsaved(): boolean {
		return this.state.mode === "global"
	}

	public override getViewType(): string {
		return PLAINTORCH_PLANNING_VIEW_TYPE
	}

	public override getDisplayText(): string {
		return this.isUnsaved ? `${contextModeLabels.global} (unsaved)` : contextModeLabels[this.state.mode]
	}

	public override getIcon(): string {
		return "git-fork"
	}

	public override getState(): Record<string, unknown> {
		return { ...super.getState(), ...this.state }
	}

	public override async setState(state: unknown, result: ViewStateResult): Promise<void> {
		const next = (state ?? {}) as Partial<PlanningViewState>
		const mode = next.mode && next.mode in contextModeLabels ? next.mode : "onrush-active"
		this.state = mode === "global" ? { mode, text: typeof next.text === "string" ? next.text : "" } : { mode }
		this.render()
		this.retitle()
		await super.setState(state, result)
	}

	/**
	 * Redraws the view header's title for the state. Obsidian draws it once, when the view opens — before its state
	 * arrives, so it would read the default context — and its own refresh redraws only the tab. The element is
	 * ItemView's `titleEl`, which the API does not declare.
	 */
	private retitle(): void {
		const header = (this as unknown as { titleEl?: HTMLElement }).titleEl
		if (header) {
			header.textContent = this.getDisplayText()
		}
	}

	public override async onClose(): Promise<void> {
		this.contentEl.empty()
	}

	/**
	 * Draws the canvas for the state. An unsaved global context takes its pins and layout from the kept text, and
	 * every change the canvas announces is kept again and the workspace asked to save it; its mark is slotted into
	 * the toolbar.
	 */
	private render(): void {
		this.contentEl.empty()
		// The plugin's own tokens are declared on this class, and inherit from here into every shadow tree.
		this.contentEl.addClass("plaintorch-root")
		this.contentEl.addClass("plaintorch-canvas-view")
		const canvas = this.contentEl.createEl("p7t-dependency-canvas") as DependencyCanvas
		canvas.singleContext = true
		canvas.mode = this.state.mode
		if (this.state.mode !== "global") {
			return
		}

		const context = parseGlobalContext(this.state.text ?? "")
		canvas.pinned = context.pinned
		canvas.savedLayout = context.layout
		canvas.addEventListener("contextChanged", (event: Event) => {
			const snapshot = (event as CustomEvent<GlobalContextSnapshot>).detail
			this.state = { mode: "global", text: serializeGlobalContext(snapshot.pinned, snapshot.layout) }
			this.app.workspace.requestSaveLayout()
		})
		const mark = document.createElement("span")
		mark.className = "plaintorch-unsaved-mark"
		mark.slot = "toolbar"
		mark.textContent = "Unsaved"
		mark.setAttribute("aria-label", "Not saved to a file yet. Save to file keeps it.")
		canvas.append(mark)
	}
}
