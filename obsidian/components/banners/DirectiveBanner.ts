import { css, html, nothing } from "@a11d/lit"
import { Directive } from "@pleiades/sdk"
import { EntityBanner } from './EntityBanner'
import { core } from ".."
import type { EditablePart } from "../editing/EditableDataLink"

/**
 * Shared base for the stellar and lunar directive banners (PEP105). The per-directive icon and the optional header
 * banner are both edited in place through the {@link EditableMedia} control: clicking either opens the media picker
 * — an entity or vault asset, an icon, or removal — and the chosen key is written back through the directives SDK.
 *
 * Setting a custom image stays two separate steps (media separation): the picker stores the file through the media
 * domain and hands back a key, and this banner only references that key onto the icon or banner field. The icon is a
 * contained square; the banner is a free-form covering image with an "add banner" affordance while it is empty.
 */
export abstract class DirectiveBanner extends EntityBanner<Directive> {
	static override get styles() {
		return css`
			${super.styles}

			p7t-editable-media.icon {
				grid-area: icon;
				align-self: center;
				width: 48px;
				height: 48px;
			}

			p7t-editable-media.banner {
				display: flex;
				width: auto;
				min-height: auto;
				border-radius: 8px;
				margin-block: -.6em .4em;
				margin-inline: 0 -.5em;
				overflow: hidden;
				box-sizing: border-box;

				&::part(media) {
					position: absolute;
					width: 100%;
					height: 100%;
					object-fit: cover;
					object-position: center;
					inset: 0;
					border-radius: 16px;
					z-index: 0;
					mask-image: linear-gradient(to bottom, rgba(0, 0, 0, .6) -10%, rgba(0, 0, 0, 0) 95%);
					pointer-events: none;
				}
			}

			/* While empty, the banner reads as an affordance to add one rather than a blank strip. */
			p7t-editable-media.banner.empty {
				border: 2px dashed color-mix(in srgb, var(--text-normal) 22%, transparent);
			}

			.top-wrapper {
				display: flex;
				justify-content: space-between;
				align-items: center;
				z-index: 0;

				& .stamp {
					z-index: 1;
				}
			}

			.render-grid {
				z-index: 1;
			}

			:host(.plaintorch-modal-content) p7t-editable-media.banner::part(media) {
				margin: -3.5em -1em;
				width: calc(100% + 2em) !important;
				height: calc(100% + 5em) !important;
			}
		`
	}

	private get mediaEntity() {
		return { entityType: 'directive', entityId: this.entity?.id ?? '' }
	}

	protected override get iconTemplate() {
		const directive = this.entity
		if (!directive) {
			return nothing
		}

		return html`
			<p7t-editable-media
				class='icon'
				icon
				.media=${directive.iconMedia}
				.value=${directive.icon ?? ''}
				.default=${this.icon}
				.entity=${this.mediaEntity}
				@change=${(e: Event) => void this.saveIcon(e)}>
			</p7t-editable-media>
		`
	}

	protected get stampTemplate() { return html`` }

	protected override get bannerImageTemplate() {
		const directive = this.entity
		if (!directive) {
			return nothing
		}

		return html`
			<div class='top-wrapper'>
				<span class='stamp'>
					${this.stampTemplate}
				</span>
				<p7t-editable-media
					class='banner ${directive.bannerMedia ? '' : 'empty'}'
					.media=${directive.bannerMedia}
					.value=${directive.banner ?? ''}
					.default=${''}
					.entity=${this.mediaEntity}
					.promptTemplate=${html`<p7t-icon-item small icon='lucide:image' text='Change Banner'>Change Banner</p7t-icon-item>`}
					@change=${(e: Event) => void this.saveBanner(e)}>
				</p7t-editable-media>
			</div>
		`
	}

	private async saveIcon(e: Event) {
		const directive = this.entity
		if (!directive) {
			return
		}

		const key = (e.target as EditablePart<string>).value?.trim() ?? ''
		await this.commitEntityEdit(async () =>
			key
				? await core.directives.setIconReference(directive.id, key)
				: await core.directives.clearIcon(directive.id))
		await this.entityRepository?.refresh(directive.id)
	}

	private async saveBanner(e: Event) {
		const directive = this.entity
		if (!directive) {
			return
		}

		const key = (e.target as EditablePart<string>).value?.trim() ?? ''
		await this.commitEntityEdit(async () =>
			key
				? await core.directives.setBannerReference(directive.id, key)
				: await core.directives.clearBanner(directive.id))
		await this.entityRepository?.refresh(directive.id)
	}
}
