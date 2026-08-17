import { component, css, html, property } from '@a11d/lit'
import { ObjectiveCollege } from '@pleiades/sdk'
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

			.named {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				font-weight: 400;
			}

			.badge {
				padding: 0.08em 0.8ch;
				border-radius: 4px;
				background: color-mix(in srgb, var(--text-normal) 15%, transparent);
				color: color-mix(in srgb, var(--text-normal) 60%, transparent);
				font-family: var(--font-interface);
			}

			p7t-icon {
				width: 1.2em;
				height: 1.2em;
			}
		`
	}

	private get descriptor() {
		return collegeDescriptorOf(this.college)
	}

	protected override get content() {
		const descriptor = this.descriptor
		if (this.mode === 'icon') {
			return html`<p7t-icon icon=${descriptor.icon}></p7t-icon>`
		}

		if (this.mode === 'badge') {
			return html`<span class='badge'>${descriptor.fullName}</span>`
		}

		return html`
			<span class='named'>
				<p7t-icon icon=${descriptor.icon}></p7t-icon>
				<span>${descriptor.name}</span>
			</span>
		`
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
