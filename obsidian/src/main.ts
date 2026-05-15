import { Plugin, type WorkspaceLeaf } from "obsidian";
import { PlaintorchBriefingView, PLAINTORCH_BRIEFING_VIEW_TYPE } from "./briefing/PlaintorchBriefingView";
import { createHelloPlaintorchEditorExtension } from "./hello/helloPlaintorchEditorExtension";

export default class PlaintorchObsidianPlugin extends Plugin {
	public override async onload(): Promise<void> {
		this.registerEditorExtension(createHelloPlaintorchEditorExtension());

		this.registerView(
			PLAINTORCH_BRIEFING_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchBriefingView(leaf)
		);

		this.addRibbonIcon("sparkles", "Open PLAINTORCH briefing", () => {
			void this.activateBriefingView();
		});

		this.addCommand({
			id: "open-plaintorch-briefing",
			name: "Open PLAINTORCH briefing",
			callback: () => {
				void this.activateBriefingView();
			}
		});
	}

	public override onunload(): void {
		this.app.workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE);
	}

	private async activateBriefingView(): Promise<void> {
		const workspace = this.app.workspace;
		workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE);
		const leaf = workspace.getLeaf(true);

		await leaf.setViewState({
			type: PLAINTORCH_BRIEFING_VIEW_TYPE,
			active: true
		});

		workspace.revealLeaf(leaf);
	}
}
