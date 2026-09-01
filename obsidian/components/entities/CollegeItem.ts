import { component, css, html, property } from '@a11d/lit'
import { ObjectiveCollege } from '@pleiades/sdk'
import { IconName } from 'components/PleiadesIcon'
import { InfoItem } from '../design/InfoItem'
import { collegeDescriptorOf } from './collegeDescriptors'

/** How much of the college to draw. */
export type CollegeItemMode = 'icon' | 'named' | 'badge'

/**
 * An objective's college (PEP095), drawn one unified way. `icon` is the glyph alone (an item's highlight lane),
 * `named` pairs it with the short name, and `badge` is the "College of X" pill a banner shows. The full name is the
 * built-in tooltip.
 */
@component('p7t-college-item')
export class CollegeItem extends InfoItem {
	@property({ type: Number }) college: ObjectiveCollege = ObjectiveCollege.Unspecified
	@property() mode: CollegeItemMode = 'named'

	static override get styles() {
		return css`
			${super.styles}

			.info-bullet {
				font-weight: 400;
			}

			.badge {
				padding: 0.08em 0.8ch;
				border-radius: 4px;
				background: color-mix(in srgb, var(--text-normal) 15%, transparent);
				color: color-mix(in srgb, var(--text-normal) 60%, transparent);
				font-family: var(--font-interface);
			}
		`
	}

	private get descriptor() {
		return collegeDescriptorOf(this.college)
	}

	protected override get bulletIcon(): IconName {
		return this.descriptor.icon
	}

	protected override get bulletText() {
		return this.descriptor.name
	}

	/** `icon` mode is the glyph alone (an item's highlight lane). */
	protected override get textHidden(): boolean {
		return this.mode === 'icon'
	}

	/** `badge` is a pill with no glyph — its own shape — so it steps outside the shared icon-text layout. */
	protected override get content() {
		if (this.mode === 'badge') {
			return html`<span class='badge'>${this.descriptor.fullName}</span>`
		}

		return super.content
	}

	protected override get tooltip() {
		return this.descriptor.fullName
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-college-item': CollegeItem
	}
}
