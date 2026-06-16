import { component, css, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { LorePage, PleiadeanDate } from '@pleiades/sdk'
import { core, IconName } from ".."
import { toRomanNumeral } from "@pleiades/sdk/helpers"

@component('p7t-lore-banner')
export class LoreBanner extends EntityBanner<LorePage> {
	
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
			${!this.entity!.beginning ? html`` : html`
				<div class="date-span">
					<p7t-date-view .date=${PleiadeanDate.fromDate(new Date(this.entity!.beginning))}></p7t-date-view>
				</div>
			`}
		`
	}

	override async fetchEntity(puck: string) {
		const entity = await core.lore.get(puck)
		if (!entity) return entity

		switch (entity.level) {
			case 'Era': this.icon = 'lore-era'; break
			case 'Cha': this.icon = 'lore-chapter'; break
			case 'Act': this.icon = 'lore-act'; break
			case 'p': this.icon = 'lore-phase'; break
			default: this.icon = 'lorepage'; break
		}
		return entity
	}

	override icon: IconName = 'lorepage'
}

declare global {
	interface HTMLTagNameMap {
		'p7t-lore-banner': LoreBanner
	}
}