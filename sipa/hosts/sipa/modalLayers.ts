/**
 * The modal dialogs open on the page, oldest first.
 *
 * A modal `<dialog>` makes everything outside itself inert — a popover shown above it too, however high in the top
 * layer — so whatever must stay interactive over a modal (the toasts) has to live inside the topmost one. Each modal
 * element renders a slot named {@link overlaySlot} inside its dialog for that, and reports here when it opens and
 * closes.
 */
const open: HTMLElement[] = []
const listeners = new Set<() => void>()

/** The slot a modal element renders inside its dialog, for content that must stay interactive above it. */
export const overlaySlot = 'overlay'

export const modalLayers = {
	/** The modal element on top, when one is open. */
	get topmost(): HTMLElement | undefined {
		return open[open.length - 1]
	},

	/** Records a modal element whose dialog just opened. */
	opened(modal: HTMLElement) {
		if (!open.includes(modal)) {
			open.push(modal)
			notify()
		}
	},

	/** Records a modal element whose dialog closed or is about to go; anything it hosts moves out first. */
	closed(modal: HTMLElement) {
		const index = open.indexOf(modal)
		if (index >= 0) {
			open.splice(index, 1)
			notify()
		}
	},

	/** Calls back whenever the topmost modal may have changed; returns the unsubscription. */
	subscribe(listener: () => void): () => void {
		listeners.add(listener)
		return () => listeners.delete(listener)
	},
}

function notify() {
	for (const listener of listeners) {
		listener()
	}
}
