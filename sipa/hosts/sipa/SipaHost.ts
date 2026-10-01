import { adoptStyles, componentStyles, type CanvasWindowHost, type GlobalContextHost, type PlatformHost, type ToastKind } from '../../host'
import { lucideIcons } from './icons'
import { ModalShell } from './Modal'
import { SuggestModalShell } from './SuggestModal'
import { sipaStyles } from './styles'
import { ToastStack } from './Toast'

export interface SipaHostOptions {
	/**
	 * A URL the renderer can load for a forward-slashed vault-relative path, or undefined when no vault is served —
	 * the standalone shell answers with its media protocol.
	 */
	readonly mediaUrl: (vaultRelativePath: string) => string | undefined
	/** The URL scheme {@link mediaUrl} produces (without the colon), so its URLs are told from glyph names. */
	readonly mediaScheme: string
	/**
	 * Saving global contexts to files, which the shell does through its own save dialog and planning windows. Without
	 * it the canvas offers no "Save to file".
	 */
	readonly globalContexts?: GlobalContextHost
	/** Popping canvas contexts out into the shell's planning windows. Without it the canvas offers no pop-out. */
	readonly canvasWindows?: CanvasWindowHost
}

/**
 * The SIPA {@link PlatformHost}: toasts in a top-right stack, dialogs and pickers on native `<dialog>`s, lucide glyphs
 * from the bundled `lucide` package, and vault images through the shell's media URLs. There is no note editor yet, so
 * opening a note says so, and following a renamed note is silent. Saving a global context to a file and popping a
 * canvas out into a window are the shell's, when it passes {@link SipaHostOptions.globalContexts} and
 * {@link SipaHostOptions.canvasWindows}.
 *
 * Also installs the shell's stylesheet and the components' document rules into the page.
 */
export function createSipaHost(options: SipaHostOptions): PlatformHost {
	adoptStyles(document, sipaStyles, componentStyles)
	const mediaPrefix = `${options.mediaScheme}:`
	return {
		name: 'sipa',
		toast: (message: string, kind: ToastKind = 'info') => {
			ToastStack.show(message, kind)
		},
		navigation: {
			openNote: async (vaultRelativePath: string) => {
				const name = vaultRelativePath.split('/').pop() ?? vaultRelativePath
				ToastStack.show(`${name} is a note in your vault. SIPA has no editor yet — open it in Obsidian.`, 'info')
			},
			followMovedNote: async () => { },
		},
		media: {
			resourceUrl: options.mediaUrl,
			isResourceUrl: (value: string) => value.startsWith(mediaPrefix),
		},
		icons: lucideIcons,
		dialogs: {
			createModal: view => new ModalShell(view),
			createSuggest: view => new SuggestModalShell(view),
		},
		globalContexts: options.globalContexts,
		canvasWindows: options.canvasWindows,
	}
}
