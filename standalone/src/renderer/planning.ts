import { html, nothing } from "@a11d/lit"
import { contextModeLabels, parseGlobalContext, serializeGlobalContext, toast, type GlobalContextSnapshot } from "@pleiades/sipa"
import { planningFileExtension, type PlanningWindowContent } from "../shared/contracts"
import { mountVaultWindow } from "./vaultWindow"

/**
 * A planning window: one dependency canvas context, alone in a window of its own — no mode switcher — as the Obsidian
 * plugin's file view (`PlaintorchGlobalFileView`) shows a planning graph file. It shows one of:
 *
 * - **a planning graph file** (`.p7tpx`, a saved global planning context — PEP102). A global context has no database
 *   home, so the file *is* the context: the canvas takes its pinned set and layout from the file and every change it
 *   announces is written back. The main process owns the file; this page reads it on opening and again whenever it
 *   changes on disk from elsewhere, which replaces the pinned set as it does in Obsidian (the layout is the canvas's
 *   own once adopted).
 * - **an unsaved global context** popped out of a canvas: held by the main process, so a reload keeps it, and marked
 *   unsaved in the title bar until its "Save to file" turns the window into that file's.
 * - **an onrush** — the active or the planning one — popped out of a canvas; the vault holds it, so there is nothing to
 *   write.
 */

let content: PlanningWindowContent | undefined
/** The context as last read or announced by the canvas: what a canvas drawn afresh starts from (not an onrush). */
let context: GlobalContextSnapshot = { pinned: [], layout: undefined }

const page = mountVaultWindow({
	container: document.getElementById("planning")!,
	subject: "the planning graph",
	content: () => {
		if (content === undefined) {
			return nothing
		}

		if (content.kind === "onrush") {
			return html`<p7t-dependency-canvas mode=${content.mode} .singleContext=${true}></p7t-dependency-canvas>`
		}

		return html`
			<p7t-dependency-canvas mode='global' .singleContext=${true} .fileBacked=${content.kind === "file"}
				.pinned=${context.pinned} .savedLayout=${context.layout}
				@contextChanged=${(event: CustomEvent<GlobalContextSnapshot>) => keep(event.detail)}
			></p7t-dependency-canvas>
		`
	}
})

const frame = document.querySelector("p7t-window-frame")
const unsavedIndicator = document.getElementById("unsaved")

/** Takes a change the canvas announced — a pin, an unpin, a drag come to rest — and hands it over to be kept. */
function keep(snapshot: GlobalContextSnapshot) {
	context = snapshot
	void page.bridge.planning.write(serializeGlobalContext(snapshot.pinned, snapshot.layout))
}

/** Takes what the window shows: on opening, after every change to its file from elsewhere, and once it is saved. */
function adopt(next: PlanningWindowContent | undefined) {
	const previous = content
	content = next
	if (!next) {
		return
	}

	if (next.kind === "file") {
		const fileName = `${next.name}.${planningFileExtension}`
		if (next.text === undefined) {
			if (previous?.kind !== "file" || previous.text !== undefined) {
				toast(`${fileName} was moved or deleted. Changes to this planning graph are not saved until it is back.`, "warning")
			}
		}
		else if (previous?.kind === "file" && previous.text === undefined) {
			toast(`${fileName} is back; changes are saved to it again.`, "info")
		}
	}

	if (next.kind !== "onrush" && next.text !== undefined) {
		const parsed = parseGlobalContext(next.text)
		context = { pinned: parsed.pinned, layout: parsed.layout }
	}

	const heading = next.kind === "file" ? next.name : contextModeLabels[next.kind === "onrush" ? next.mode : "global"]
	document.title = `${heading}${next.kind === "unsaved" ? " (unsaved)" : ""} — PLAINTORCH Planning`
	if (frame) {
		frame.heading = `${heading} — PLAINTORCH Planning`
	}

	unsavedIndicator?.toggleAttribute("hidden", next.kind !== "unsaved")
	page.repaint()
}

page.bridge.planning.onChanged(adopt)
void page.bridge.planning.read().then(adopt)
