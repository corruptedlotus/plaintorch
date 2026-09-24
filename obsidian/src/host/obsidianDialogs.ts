import { Modal, SuggestModal, type App } from "obsidian"
import type { DialogHost, DialogShell, ModalView, SuggestShell, SuggestView } from "@pleiades/sipa"

/** The class Obsidian's result container gets when a picker asks for a grid (styled in the plugin's styles.css). */
const gridClass = "plaintorch-suggest-grid"

/** The SIPA dialogs on Obsidian's own modals, so a dialog looks and behaves as every other Obsidian modal does. */
export function createObsidianDialogs(app: App): DialogHost {
	return {
		createModal: view => new ObsidianModalShell(app, view),
		createSuggest: view => new ObsidianSuggestShell(app, view),
	}
}

/** An Obsidian modal hosting a SIPA modal's content element. */
class ObsidianModalShell extends Modal implements DialogShell {
	constructor(app: App, private readonly view: ModalView) {
		super(app)
	}

	override onOpen(): void {
		this.titleEl.setText(this.view.title)
		this.contentEl.append(this.view.contentEl)
		this.view.handleOpen()
	}

	override onClose(): void {
		this.view.handleClose()
		this.view.contentEl.remove()
	}
}

/** An Obsidian suggest modal forwarding to a SIPA picker. */
class ObsidianSuggestShell<T> extends SuggestModal<T> implements SuggestShell {
	constructor(app: App, private readonly view: SuggestView<T>) {
		super(app)
		if (view.placeholder) {
			this.setPlaceholder(view.placeholder)
		}

		if (view.layout === "grid") {
			this.resultContainerEl.addClass(gridClass)
		}
	}

	getSuggestions(query: string): T[] | Promise<T[]> {
		return this.view.getSuggestions(query)
	}

	renderSuggestion(value: T, el: HTMLElement): void {
		this.view.renderSuggestion(value, el)
	}

	// Choices are reported through selectSuggestion, which the picker handles (closing first, as Obsidian does).
	onChooseSuggestion(): void { }

	override selectSuggestion(value: T, evt: MouseEvent | KeyboardEvent): void {
		this.view.selectSuggestion(value, evt)
	}

	override onOpen(): void | Promise<void> {
		const opening = super.onOpen()
		this.view.handleOpen()
		return opening
	}

	override onClose(): void {
		super.onClose()
		this.view.handleClose()
	}

	setQuery(query: string): void {
		this.inputEl.value = query
		this.inputEl.dispatchEvent(new Event("input"))
		this.inputEl.focus()
	}
}
