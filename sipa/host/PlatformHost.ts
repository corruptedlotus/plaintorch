import type { DialogHost } from './dialogs'

/**
 * What the UI asks of the application it runs in (PEP110, *Sunnyside Mk1 — SIPA*). The components depend on this
 * seam instead of on Obsidian: the Obsidian plugin provides one implementation, the standalone shell another, and
 * everything platform-specific — how a message is shown, how a note is opened, where an image is loaded from, how
 * an icon is drawn — lives behind it.
 *
 * Vault reads and writes are not the host's business: they go through the core client, and the watcher keeps the
 * files in step. The host only covers what the core cannot do from where it sits.
 */
export interface PlatformHost {
	/** Which application this is, for the rare surface that has to say so — `obsidian`, `sipa`. */
	readonly name: string

	/**
	 * Shows a short, self-dismissing message. `kind` says what the message reports; a host may colour or time it by
	 * kind, or ignore it (Obsidian's Notice has no kinds).
	 */
	toast(message: string, kind?: ToastKind): void

	readonly navigation: NavigationHost
	readonly media: MediaHost
	readonly icons: IconHost

	/** How modal dialogs and pickers are shown; the dialog classes reach it through `ModalBase` / `SuggestModalBase`. */
	readonly dialogs: DialogHost

	/**
	 * Saving a global dependency context to a file of its own. Optional: a host without file-backed canvases leaves
	 * it out, and the canvas then offers no "Save to file".
	 */
	readonly globalContexts?: GlobalContextHost

	/**
	 * Popping a dependency canvas's context out on its own — a window in the standalone shell, a tab in Obsidian.
	 * Optional: without it the canvas offers no pop-out.
	 */
	readonly canvasPopOut?: CanvasPopOutHost
}

/** What a {@link PlatformHost.toast | toast} reports. */
export type ToastKind = 'info' | 'success' | 'warning' | 'error'

/** How notes are brought in front of the user. */
export interface NavigationHost {
	/**
	 * Opens a vault-relative note (either slash). With `waitForFile`, tolerates a write still draining in the core
	 * (PEP110): the note may land a beat after the call that created or moved it, so the host waits for the file —
	 * announcing the wait up front when `pending` — and opens it once it is there, or gives up quietly.
	 */
	openNote(vaultRelativePath: string, options?: OpenNoteOptions): Promise<void>

	/**
	 * A note the user is working on moved, typically because its entity was renamed. A host that shows notes follows
	 * it unless it is already the note in front; a host that does not show notes stays silent — this is never an
	 * explicit request, so it must not announce anything.
	 */
	followMovedNote(vaultRelativePath: string, pending: boolean): Promise<void>
}

export interface OpenNoteOptions {
	/** Wait for the file to appear before opening it (see {@link NavigationHost.openNote}). */
	readonly waitForFile?: boolean
	/** The core reported the write still pending; announce the short wait. Only meaningful with `waitForFile`. */
	readonly pending?: boolean
}

/** Where vault files — custom icons, banner images — are loaded from. */
export interface MediaHost {
	/**
	 * A URL an `<img>` or a CSS image in this application can load, for a forward-slashed vault-relative path, or
	 * undefined when the host cannot reach the file. Synchronous: the core only reports paths of files that exist.
	 */
	resourceUrl(vaultRelativePath: string): string | undefined

	/** Whether a string is a URL {@link resourceUrl} produced — so a renderer can tell an image from a glyph name. */
	isResourceUrl(value: string): boolean
}

/** Where the lucide glyphs come from. The Pleiades glyphs ship with the package and need no host. */
export interface IconHost {
	/**
	 * A fresh SVG element drawing the lucide icon of that name — bare, or prefixed `lucide:` or `lucide-` — or
	 * undefined when the host has no icon by that name.
	 */
	lucide(name: string): SVGElement | undefined

	/** Every lucide icon the host can draw, by bare name, for the icon picker. */
	lucideNames(): readonly string[]
}

/** File-backed global dependency contexts (the `.p7tpx` files). */
export interface GlobalContextHost {
	/**
	 * Saves `text` — a serialised global context — as a new planning graph file and opens it in its file-backed view.
	 * Where the file goes is the host's to ask: Obsidian asks for a name and creates it at the vault root, refusing a
	 * name already taken; the standalone shell shows a save dialog, which may put it anywhere and confirms replacing a
	 * file. A host reports its own refusals.
	 *
	 * `origin` is the element that asked. Saved from a popped-out global context (see {@link CanvasPopOutHost}), the
	 * file opens where that context was — its window or tab becomes the file's — rather than somewhere new; a host
	 * that knows the asker otherwise (the shell, by the window that sent the request) may ignore it.
	 *
	 * Resolves the path the file was saved to, as the user knows it (vault-relative inside the vault), or undefined
	 * when nothing was saved — the user backed out, or the host refused. Rejects when the file could not be written.
	 */
	saveAndOpen(text: string, origin?: HTMLElement): Promise<{ readonly path: string } | undefined>
}

/**
 * A dependency canvas context to pop out: an onrush, by which one — the one under way or the one being planned — or a
 * global context, as the text a planning graph file would hold.
 */
export type CanvasPopOut =
	| { readonly mode: 'onrush-active' | 'onrush-planning' }
	| { readonly mode: 'global', readonly text: string }

/** Dependency canvas contexts popped out on their own: the standalone shell's planning windows, Obsidian's tabs. */
export interface CanvasPopOutHost {
	/** What the pop-out button says it does, as its tooltip: "Open in a new window", "Open in a new tab". */
	readonly label: string

	/**
	 * Opens a context on its own, as a planning graph file opens. An onrush has one window or tab, brought forward when
	 * it is already open; a global context opens a new one that holds it unsaved until it is saved to a file. Rejects
	 * when nothing could be opened.
	 */
	popOut(context: CanvasPopOut): Promise<void>
}
