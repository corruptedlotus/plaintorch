import { component, css, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { LorePage } from '@pleiades/sdk'
import { core, IconName, ReactiveBinder } from ".."
import { toRomanNumeral } from "@pleiades/sdk/helpers"

@component('p7t-lore-banner')
export class LoreBanner extends EntityBanner<LorePage> {

	/** Two-way binds the beginning date, persisting it through the lore SDK (file-first frontmatter write). */
	protected binder = new ReactiveBinder<LorePage>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			if (keyPath !== 'beginning') return
			const entity = this.entity!
			await this.commitEntityEdit(async () => await core.lore.update(
				entity.id,
				entity.beginning ? { beginning: entity.beginning } : { clearBeginning: true }))
		}
	})

	static override get styles() {
		return css`
			${super.styles}

			:host::part(sub-heading) {
				font-weight: 300;
				font-size: .9em;
				margin-top: -.2em;
				opacity: 1;
			}

			.status::part(icon) {
				width: 1.4em;
				height: 1.4em;
			}

			.date-span {
				display: flex;
				align-items: center;
				font-weight: 300;
				gap: .5em;
				opacity: .7;
				font-size: 1em;

				& .label {
					font-size: .7em;
					text-transform: uppercase;
					letter-spacing: .08em;
					opacity: .7;
				}
			}
		`
	}

	protected override get secondary() {
		return html``
	}

	protected override get preHeadingTemplate() {
		switch (this.entity?.level) {
			case 'Era':
				return html`<span>Era ${this.entity?.era}</span>`
				break
			case 'Cha':
				return html`<span>Era ${this.entity?.era}: ${this.entity?.parent?.title} / Chapter ${toRomanNumeral(this.entity?.chapter!)}</span>`
				break
			case 'Act':
				return html`<span>Chapter ${toRomanNumeral(this.entity?.chapter!)}: ${this.entity?.parent?.title} / Act ${toRomanNumeral(this.entity?.act!)}</span>`
				break
			case 'p':
				return html`<span>Act ${toRomanNumeral(this.entity?.act!)}: ${this.entity?.parent?.title} / φ${this.entity?.phase!}</span>`
				break
			default:
				return html`<span>Rogue Lorepage</span>`
				break
		}
	}

	protected override get headingTemplate() {
		return html`<span>${this.entity?.title}</span>`
	}

	protected override get actions() {
		return html`
			<div class="date-span">
				<span class='label'>Begins</span>
				<p7t-editable-date ${this.binder.bind('beginning')}></p7t-editable-date>
			</div>
		`
	}

	protected override readonly entityTypeName = 'LorePage' as const

	protected override get resolvedIcon(): IconName {
		switch (this.entity?.level) {
			case 'Era': return 'lore-era'
			case 'Cha': return 'lore-chapter'
			case 'Act': return 'lore-act'
			case 'p': return 'lore-phase'
			default: return 'lorepage'
		}
	}

	override icon: IconName = 'lorepage'
}

declare global {
	interface HTMLTagNameMap {
		'p7t-lore-banner': LoreBanner
	}
}