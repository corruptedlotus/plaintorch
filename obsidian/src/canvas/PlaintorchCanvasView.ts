import { ItemView, type WorkspaceLeaf } from "obsidian"
import type { CanvasContextMode, DependencyCanvas } from "@pleiades/sipa"

export const PLAINTORCH_CANVAS_VIEW_TYPE = "plaintorch-dependency-canvas"

/**
 * The leaf the dependency canvas gets to itself.
 *
 * The same component the briefing shows in a tab, given the whole pane: a graph is worth more room than a
 * tab beside a hero banner allows, and it can then sit beside the note of whatever is being planned. The
 * view is a shell — two classes and one element — because everything it shows belongs to the component.
 */
export class PlaintorchCanvasView extends ItemView {
	private canvas?: DependencyCanvas

	public constructor(leaf: WorkspaceLeaf) {
		super(leaf)
	}

	public override getViewType(): string {
		return PLAINTORCH_CANVAS_VIEW_TYPE
	}

	public override getDisplayText(): string {
		return "PLAINTORCH Dependencies"
	}

	public override getIcon(): string {
		return "git-fork"
	}

	public override async onOpen(): Promise<void> {
		this.contentEl.empty()
		// The plugin's own tokens are declared on this class, and inherit from here into every shadow tree.
		this.contentEl.addClass("plaintorch-root")
		this.contentEl.addClass("plaintorch-canvas-view")
		this.canvas = this.contentEl.createEl("p7t-dependency-canvas") as DependencyCanvas
	}

	/** Switches the hosted canvas to a context mode — how the "new global planning" command opens on global. */
	public setMode(mode: CanvasContextMode): void {
		if (this.canvas) {
			this.canvas.mode = mode
		}
	}

	public override async onClose(): Promise<void> {
		this.canvas = undefined
		this.contentEl.empty()
	}
}
