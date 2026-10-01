import { html, nothing } from "@a11d/lit"
import { parseGlobalContext, serializeGlobalContext, toast, type GlobalContextSnapshot } from "@pleiades/sipa"
import { planningFileExtension, type PlanningFileSnapshot } from "../shared/contracts"
import { mountVaultWindow } from "./vaultWindow"

/**
 * A planning window: one planning graph file (`.p7tpx`, a saved global planning context — PEP102) in the file-backed
 * dependency canvas, as the Obsidian plugin's file view (`PlaintorchGlobalFileView`) shows it.
 *
 * A global context has no database home, so the file *is* the context: the canvas takes its pinned set and layout
 * from the file, locked to that one context — no mode switcher — and every change it announces is written back. The
 * main process owns the file; this page reads it on opening and again whenever it changes on disk from elsewhere,
 * which replaces the pinned set as it does in Obsidian (the layout is the canvas's own once adopted).
 */

let file: PlanningFileSnapshot | undefined
/** The context as last read from the file or announced by the canvas: what a canvas drawn afresh starts from. */
let context: GlobalContextSnapshot = { pinned: [], layout: undefined }

const page = mountVaultWindow({
	container: document.getElementById("planning")!,
	subject: "the planning graph",
	content: () => file === undefined ? nothing : html`
		<p7t-dependency-canvas mode='global' .fileBacked=${true} .pinned=${context.pinned} .savedLayout=${context.layout}
			@contextChanged=${(event: CustomEvent<GlobalContextSnapshot>) => keep(event.detail)}
		></p7t-dependency-canvas>
	`
})

/** Takes a change the canvas announced — a pin, an unpin, a drag come to rest — and has it written to the file. */
function keep(snapshot: GlobalContextSnapshot) {
	context = snapshot
	void page.bridge.planning.write(serializeGlobalContext(snapshot.pinned, snapshot.layout))
}

/** Takes the file as the main process read it: on opening, and after every change on disk from elsewhere. */
function adopt(snapshot: PlanningFileSnapshot | undefined) {
	const previous = file
	file = snapshot
	if (!snapshot) {
		return
	}

	const fileName = `${snapshot.name}.${planningFileExtension}`
	if (snapshot.text === undefined) {
		if (previous === undefined || previous.text !== undefined) {
			toast(`${fileName} was moved or deleted. Changes to this planning graph are not saved until it is back.`, "warning")
		}
	}
	else {
		if (previous !== undefined && previous.text === undefined) {
			toast(`${fileName} is back; changes are saved to it again.`, "info")
		}

		const parsed = parseGlobalContext(snapshot.text)
		context = { pinned: parsed.pinned, layout: parsed.layout }
	}

	document.title = `${snapshot.name} — PLAINTORCH Planning`
	const frame = document.querySelector("p7t-window-frame")
	if (frame) {
		frame.heading = document.title
	}

	page.repaint()
}

page.bridge.planning.onChanged(adopt)
void page.bridge.planning.read().then(adopt)
