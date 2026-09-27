/**
 * How a host shows the UI's dialogs. The dialog classes never see this: they extend {@link ModalBase} or
 * {@link SuggestModalBase}, which hand the host a view of themselves and get a shell back — so swapping the dialog
 * engine (Obsidian's modals, the standalone shell's own) never touches them.
 */
export interface DialogHost {
	/** A modal dialog around a view's content. Nothing shows until the shell is opened. */
	createModal(view: ModalView): DialogShell

	/** A searching picker: a query input over a list (or grid) of suggestions. Nothing shows until opened. */
	createSuggest<T>(view: SuggestView<T>): SuggestShell
}

/** What a modal dialog shows and how it reports its lifecycle to the class behind it. */
export interface ModalView {
	/** The dialog's body. The view owns it; the host puts it in place while the dialog is up. */
	readonly contentEl: HTMLElement

	/** The heading, read when the dialog opens (later changes go through {@link DialogShell.setTitle}). */
	readonly title: string

	/** The dialog is up and its content attached. */
	handleOpen(): void

	/**
	 * The dialog went away — by its close button, Escape, the backdrop, or {@link DialogShell.close}. Called once per
	 * opening.
	 */
	handleClose(): void
}

/** A dialog the host created, driven by the class behind it. */
export interface DialogShell {
	open(): void
	close(): void
	setTitle(title: string): void
}

/** How a picker lays its suggestions out: a list of rows, or a grid of tiles. */
export type SuggestLayout = 'list' | 'grid'

/** What a picker searches and shows, and how it reports choices and its lifecycle. */
export interface SuggestView<T> {
	readonly placeholder: string
	readonly layout: SuggestLayout

	/** The suggestions for a query — `''` when the picker opens, then on every edit. Stale answers are dropped. */
	getSuggestions(query: string): T[] | Promise<T[]>

	/** Draws one suggestion into its row (or tile). The element is the host's; draw into it, don't replace it. */
	renderSuggestion(value: T, el: HTMLElement): void

	/** A suggestion was chosen, by click or Enter. What happens next — closing or not — is the view's call. */
	selectSuggestion(value: T, evt: MouseEvent | KeyboardEvent): void

	handleOpen(): void
	handleClose(): void
}

/** A picker the host created, driven by the class behind it. */
export interface SuggestShell {
	open(): void
	close(): void

	/** Replaces the query, as if typed, and searches again. */
	setQuery(query: string): void
}
