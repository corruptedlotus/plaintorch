import { component, css, eventListener, html, nothing } from "@a11d/lit"
import { CardComponent } from "components/design"
import { core, DerivedRef } from ".."

/**
 * The day-level agenda, stacked above the Polaris cycle card. It surfaces unbound attentives requiring
 * attention (same-day/24h and previous unattended) and upcoming eventives (7 days) from `core.repos.agenda`,
 * which stays live over the feed like the briefing. Bound attentives live on the cycle card, not here.
 */
@component('p7t-briefing-agenda')
export class BriefingCardAgenda extends CardComponent {
	private readonly agendaRef = new DerivedRef(this, core.repos.agenda)

	private get agenda() {
		return this.agendaRef.value
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				background: none;
				border: none;
				padding-block: .2em;
			}

			.section {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				gap: .1em;
			}

			.section + .section {
				margin-top: .6em;
			}

			.section-title {
				font-size: .8em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .04em;
				opacity: .55;
				margin-inline-start: .4em;
				margin-bottom: .2em;
			}

			.list {
				display: flex;
				flex-direction: column;
				align-items: stretch;
			}

			.empty {
				font-weight: 400;
				opacity: .6;
				font-size: 1.1em;
				padding: .4em .5em;
			}

			.count-badge {
				font-family: var(--font-text);
				font-size: .6em;
				font-weight: 600;
				background-color: color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: .8em;
				padding: .1em .6em;
				margin-inline-start: .5ch;
				vertical-align: middle;
			}
		`
	}

	protected override get preHeadingTemplate() {
		return nothing
	}

	override get headingTemplate() {
		const count = this.agenda?.attentives.length ?? 0
		return html`
			<span>
				Agenda
				${this.collapsed && count > 0 ? html`<span class='count-badge'>${count}</span>` : nothing}
			</span>
		`
	}

	protected override get content() {
		const attentives = this.agenda?.attentives ?? []
		const eventives = this.agenda?.eventives ?? []

		if (attentives.length === 0 && eventives.length === 0) {
			return html`<span class='empty'>Nothing needs your attention.</span>`
		}

		return html`
			${attentives.length === 0 ? nothing : html`
				<div class='section'>
					<span class='section-title'>Requires Attention</span>
					<div class='list'>
						${attentives.map(attentive => html`
							<p7t-attentive-item interactive .attentive=${attentive}></p7t-attentive-item>
						`)}
					</div>
				</div>
			`}
			${eventives.length === 0 ? nothing : html`
				<div class='section'>
					<span class='section-title'>Upcoming</span>
					<div class='list'>
						${eventives.map(eventive => html`
							<p7t-eventive-item interactive .eventive=${eventive}></p7t-eventive-item>
						`)}
					</div>
				</div>
			`}
		`
	}

	/** An attentive was resolved — a done occurrence drops out of the agenda, so refetch the record. */
	@eventListener('attentivechange')
	protected onAttentiveChange(e: Event) {
		e.stopPropagation()
		void this.agendaRef.refresh()
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-agenda': BriefingCardAgenda
	}
}
