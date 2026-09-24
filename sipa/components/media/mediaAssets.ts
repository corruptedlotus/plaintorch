import type { MediaReference } from '@pleiades/sdk'
import { host } from '../../host'

/**
 * The shared asset folder name, matching the core (`VaultMediaService.AssetFolderName`). Its leading underscore
 * keeps it out of markdown discovery, and a `vault:` file resolves to a deterministic path beneath it — which is
 * what lets a vault image preview without waiting on a resolved companion from the core (PEP105).
 */
export const ASSET_FOLDER = '_media'

/**
 * Resolves a vault-relative media path (as the core serialises for a directive icon or banner, PEP105) to a URL the
 * host can load in an `<img src>` or a CSS `background-image`. Returns undefined when the path is empty or the host
 * cannot reach the file.
 */
export function resolveMediaUrl(vaultRelativePath: string | undefined): string | undefined {
	return vaultRelativePath ? host.media.resourceUrl(vaultRelativePath) : undefined
}

/** Resolves a media companion's custom image to a resource URL (PEP105); undefined for a glyph or when missing. */
export function mediaUrl(media: MediaReference | undefined): string | undefined {
	return resolveMediaUrl(media?.path)
}

/**
 * Whether an icon source is an image to draw as a picture rather than a glyph name: a URL the host produced, a
 * web, data or blob URL, or anything path-like.
 */
export function isImageSource(source: string): boolean {
	return /^(https?|data|blob):/i.test(source) || source.includes('/') || host.media.isResourceUrl(source)
}

/**
 * Resolves the icon a media companion should show (PEP105): its custom image (self or vault) as a resource URL,
 * its chosen glyph name, or the supplied default when it resolves to neither.
 */
export function resolveMediaIcon(media: MediaReference | undefined, defaultGlyph: string): string {
	if (media?.path) {
		return resolveMediaUrl(media.path) ?? defaultGlyph
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
