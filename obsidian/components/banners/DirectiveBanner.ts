import { html, nothing } from "@a11d/lit"
import { App } from "obsidian"
import { Directive } from "@pleiades/sdk"
import { EntityBanner } from './EntityBanner'
import { core, mediaUrl, pickImageFile, resolveMediaIcon } from ".."

/**
 * Shared base for the stellar and lunar directive banners (PEP105). Adds the per-directive icon — a custom
 * uploaded image or a chosen glyph, falling back to the kind default carried in {@link icon} — and an optional
 * banner header image, plus the affordances to set, replace, and clear them through the directives SDK.
 *
 * The core owns writing assets into the vault; this banner only picks the file, uploads its bytes, and renders
 * the resolved paths the API returns.
 */
export abstract class DirectiveBanner extends EntityBanner<Directive> {
	/** The App used to resolve vault resource URLs — the injected one, or the global as the banners already do. */
	protected get mediaApp(): App | undefined {
		return this.app ?? (window as any).app as App
	}

	protected override get resolvedIcon(): string {
		return resolveMediaIcon(this.entity?.iconMedia, this.mediaApp, this.icon)
	}

	protected override get bannerImageTemplate() {
		const url = mediaUrl(this.entity?.bannerMedia, this.mediaApp)
		return url
			? html`<div class='banner-image' style="background-image: url('${url}')"></div>`
			: nothing
	}

	protected override get actions() {
		const directive = this.entity
		if (!directive) {
			return html``
		}

		const hasIcon = !!directive.iconMedia
		const hasBanner = !!directive.bannerMedia

		return html`
			<p7t-button icon='lucide:image' @click=${() => void this.pickMedia('icon')}>
				<span>${hasIcon ? 'Change icon' : 'Set icon'}</span>
			</p7t-button>
			${!hasIcon ? nothing : html`
				<p7t-button icon='lucide:x' @click=${() => void this.clearMedia('icon')}>
					<span>Clear</span>
				</p7t-button>
			`}
			<p7t-button icon='lucide:panel-top' @click=${() => void this.pickMedia('banner')}>
				<span>${hasBanner ? 'Change banner' : 'Set banner'}</span>
			</p7t-button>
			${!hasBanner ? nothing : html`
				<p7t-button icon='lucide:x' @click=${() => void this.clearMedia('banner')}>
					<span>Clear</span>
				</p7t-button>
			`}
		`
	}

	private async pickMedia(kind: 'icon' | 'banner') {
		const directive = this.entity
		if (!directive) {
			return
		}

		const picked = await pickImageFile()
		if (!picked) {
			return
		}

		await this.commitEntityEdit(async () =>
			kind === 'icon'
				? await core.directives.uploadIcon(directive.id, picked.fileName, picked.bytes)
				: await core.directives.uploadBanner(directive.id, picked.fileName, picked.bytes))
		await this.entityRepository?.refresh(directive.id)
	}

	private async clearMedia(kind: 'icon' | 'banner') {
		const directive = this.entity
		if (!directive) {
			return
		}

		await this.commitEntityEdit(async () =>
			kind === 'icon'
				? await core.directives.clearIcon(directive.id)
				: await core.directives.clearBanner(directive.id))
		await this.entityRepository?.refresh(directive.id)
	}
}
