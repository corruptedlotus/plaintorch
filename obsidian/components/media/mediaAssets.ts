import type { App } from 'obsidian'
import type { MediaReference } from '@pleiades/sdk'

/**
 * Resolves a vault-relative media path (as the core serialises for a directive icon or banner, PEP105) to an
 * Obsidian resource URL usable in an `<img src>` or a CSS `background-image`. Returns undefined when the path
 * is empty or the file is not present in the vault.
 */
export function resolveMediaUrl(app: App | undefined, vaultRelativePath: string | undefined): string | undefined {
	if (!app || !vaultRelativePath) {
		return undefined
	}

	const file = app.vault.getFileByPath(vaultRelativePath)
	return file ? app.vault.getResourcePath(file) : undefined
}

/** Resolves a media companion's custom image to a resource URL (PEP105); undefined for a glyph or when missing. */
export function mediaUrl(media: MediaReference | undefined, app: App | undefined): string | undefined {
	return resolveMediaUrl(app, media?.path)
}

/**
 * Resolves the icon a media companion should show (PEP105): its custom image (self or vault) as a resource URL,
 * its chosen glyph name, or the supplied default when it resolves to neither.
 */
export function resolveMediaIcon(media: MediaReference | undefined, app: App | undefined, defaultGlyph: string): string {
	if (media?.path) {
		return resolveMediaUrl(app, media.path) ?? defaultGlyph
	}

	// A glyph key is used as-is; an unresolved custom-media key (missing file) falls back to the default.
	if (media?.type === 'icon' && media.key) {
		return media.key
	}

	return defaultGlyph
}

/** A media file the user picked, ready to hand to the directives SDK's upload helpers (PEP105). */
export interface PickedMedia {
	fileName: string
	bytes: Uint8Array
}

/**
 * Opens the OS file picker for an image and reads the chosen file's bytes (PEP105). Resolves undefined when the
 * user cancels. The caller uploads the result through the directives SDK, which owns writing it into the vault —
 * this helper never touches the vault itself.
 */
export function pickImageFile(accept = 'image/*'): Promise<PickedMedia | undefined> {
	return new Promise(resolve => {
		const input = document.createElement('input')
		input.type = 'file'
		input.accept = accept
		input.style.display = 'none'

		let settled = false
		const settle = (value: PickedMedia | undefined) => {
			if (settled) {
				return
			}

			settled = true
			input.remove()
			resolve(value)
		}

		input.addEventListener('change', async () => {
			const file = input.files?.[0]
			if (!file) {
				settle(undefined)
				return
			}

			const buffer = await file.arrayBuffer()
			settle({ fileName: file.name, bytes: new Uint8Array(buffer) })
		})

		// A cancelled picker fires no 'change'. The 'cancel' event covers it directly; the focus-return check is
		// a fallback so a cancel can never leave the promise hanging.
		input.addEventListener('cancel', () => settle(undefined))
		window.addEventListener('focus', () => {
			setTimeout(() => {
				if (!input.files || input.files.length === 0) {
					settle(undefined)
				}
			}, 500)
		}, { once: true })

		document.body.appendChild(input)
		input.click()
	})
}
