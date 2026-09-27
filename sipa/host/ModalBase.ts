import type { DialogShell } from './dialogs'
import { getHost } from './provider'

/**
 * Base of the UI's modal dialogs, shown by whatever application hosts the UI (see `DialogHost`).
 *
 * It keeps the shape the dialogs were written against — draw into {@link contentEl} in {@link onOpen}, tidy up in
 * {@link onClose}, {@link open} and {@link close} from outside — so a dialog is platform-neutral without being
 * rewritten. The content element carries `plaintorch-root` (the scope the `--p7t-accent-*` tokens are defined on) and
 * is emptied after every {@link onClose}, so a dialog neither sets the one nor clears the other itself.
 */
export abstract class ModalBase {
	/** The dialog's body: draw into it while open. */
	readonly contentEl: HTMLElement = document.createElement('div')

	private title = ''
	private shell?: DialogShell

	constructor() {
		this.contentEl.classList.add('plaintorch-root')
	}

	/** Sets the heading — before opening or while open. */
	setTitle(title: string): void {
		this.title = title
		this.shell?.setTitle(title)
	}

	/** Shows the dialog. Opening one that is already open does nothing. */
	open(): void {
		if (this.shell) {
			return
		}

		const modal = this
		this.shell = getHost().dialogs.createModal({
			contentEl: this.contentEl,
			get title() { return modal.title },
			handleOpen: () => void this.onOpen(),
			handleClose: () => {
				this.shell = undefined
				const closing = this.onClose()
				if (!(closing instanceof Promise)) {
					this.contentEl.replaceChildren()
					return
				}

				// An asynchronous onClose finishes first; the content is then cleared — unless the dialog reopened meanwhile.
				void closing.finally(() => {
					if (!this.shell) {
						this.contentEl.replaceChildren()
					}
				})
			},
		})
		this.shell.open()
	}

	/** Dismisses the dialog; {@link onClose} follows. */
	close(): void {
		this.shell?.close()
	}

	/** The dialog is up: draw its content. */
	onOpen(): void | Promise<void> { }

	/** The dialog went away, however it was dismissed. The content is emptied afterwards. */
	onClose(): void | Promise<void> { }
}
