import { Component, component, css, html, property } from '@a11d/lit'
import type { MediaReference } from '@pleiades/sdk'
import { ASSET_FOLDER, isImageSource, resolveMediaIcon, resolveMediaUrl } from './mediaAssets'

/** The `vault:` scheme, whose file resolves to a deterministic path under the shared asset folder. */
const vaultScheme = 'vault:'

/** The `media:` scheme, whose self asset folder is only known to the core, so it needs a resolved companion. */
const selfScheme = 'media:'

/**
 * Displays a media companion (PEP105) dynamically: a custom uploaded image (self or vault) as a full-colour
 * picture, a Pleiades or lucide glyph as a tintable icon, or — when the field resolves to neither — a caller-named
 * default. It wraps {@link PleiadesIcon}, which already renders an image or a glyph mask as appropriate, and adds
 * the media-field concerns on top: the resolution from a {@link MediaReference}, and the empty-state default.
 *
 * `icon` forces a square frame (contained) for the common case of a small icon; leave it off where the media is
 * free-form, such as a banner.
 */
@component('p7t-media')
export class MediaView extends Component {
	/** The resolved media companion from the core; its custom-image path, when present, takes precedence. */
	@property({ type: Object }) media?: MediaReference

	/**
	 * A raw media key to show when no resolved {@link media} companion is on hand — a glyph or lucide name renders
	 * directly, while a scheme-carrying (`media:`/`vault:`) key has no resolvable path here and falls to the default.
	 * Lets a surface preview a freshly chosen glyph before the core round-trips a resolved companion back.
	 */
	@property() mediaKey?: string

	/** The glyph shown when the field is empty or a custom image is missing. Empty renders nothing. */
	@property() default = ''

	/** Constrains the frame to a contained square, for icon (as opposed to banner) use. */
	@property({ type: Boolean, reflect: true }) icon = false

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				justify-content: center;
				width: 1.2em;
				height: 1.2em;
			}

			:host([icon]) {
				aspect-ratio: 1 / 1;
			}

			p7t-icon {
				width: 100%;
				height: 100%;
			}

			/*
			 * In icon mode a custom picture is inset to ~80% of the frame (10% padding a side), so it reads with the
			 * same breathing room a glyph mask has by design instead of crowding the square's edges. Glyphs are left
			 * to fill as before.
			 */
			:host([icon]) p7t-icon.custom {
				padding: 10%;
				box-sizing: border-box;
			}

			/* A banner (non-icon) custom image fills the frame edge-to-edge; the icon path stays a contained glyph mask. */
			img {
				width: 100%;
				height: 100%;
				object-fit: cover;
				border-radius: inherit;
			}
		`
	}

	/** The source handed to {@link PleiadesIcon}: a resolved image URL, a glyph name, or the default (possibly empty). */
	private get source(): string {
		if (this.media) {
			return resolveMediaIcon(this.media, this.default)
		}

		const key = this.mediaKey?.trim()
		if (!key) {
			return this.default
		}

		// A vault file lives at a deterministic path, so it previews without waiting on a resolved companion; a
		// self (media:) file's folder is only known to the core, so it falls to the default until one arrives.
		if (key.startsWith(vaultScheme)) {
			return resolveMediaUrl(`${ASSET_FOLDER}/${key.slice(vaultScheme.length)}`) ?? this.default
		}

		return key.startsWith(selfScheme) ? this.default : key
	}

	protected override get template() {
		const source = this.source
		if (!source) {
			return html``
		}

		// A free-form (non-icon) custom picture — a banner — renders as a covering image; a glyph, or anything in
		// icon mode, stays a contained p7t-icon. A custom picture in icon mode is marked so it can be inset (above).
		const custom = isImageSource(source)
		return !this.icon && custom
			? html`<img part='image' src=${source} alt='' />`
			: html`<p7t-icon class=${custom ? 'custom' : ''} .icon=${source}></p7t-icon>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-media': MediaView
	}
}
