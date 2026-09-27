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
	 * Creates `fileName` at the vault root holding `text` and opens it in its file-backed view. A name already taken
	 * is refused — nothing is written — and reported as `created: false`. `path` is the normalised path either way.
	 */
	createAndOpen(fileName: string, text: string): Promise<{ readonly created: boolean, readonly path: string }>
}
