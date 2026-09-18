import { Component, component, css, html, nothing, property, PropertyValues, repeat, state } from '@a11d/lit'
import { Attentive, Eventive } from '@pleiades/sdk'
import { core } from '..'

/**
 * One declarative's own agenda, listed in place — the fate's eventives or the decree's attentives, scoped to that single
 * declarative rather than the system-wide {@link https PolarisAgenda} the briefing shows.
 *
 * Modelled on the onrush detail window's orders list and the timeframes editor: a declarative's occurrences are not a
 * tracked repository, so the list is fetched here and re-read after an interaction rather than observed through the
 * store. A fate is handed as {@link fateId}, a decree as {@link decreeId}; exactly one is set, which side is fetched.
 *
 * Only materialized (hardened) occurrences are listed — the same rows the `/api/eventives` and `/api/attentives`
 * endpoints return — so a purely-recurring declarative with no interactions yet reads as empty until one is projected.
 */
@component('p7t-declarative-agenda')
export class DeclarativeAgendaEditor extends Component {
	/** The fate whose eventives these are, when this agenda belongs to a fate. */
	@property() fateId = ''

	/** The decree whose attentives these are, when this agenda belongs to a decree. */
	@property() decreeId = ''

	@state() private eventives: readonly Eventive[] = []
	@state() private attentives: readonly Attentive[] = []
	@state() private loading = true

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .6em;
			}

			.heading {
				display: flex;
				align-items: center;
				gap: .4em;
				font-weight: 600;
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.rows {
				display: flex;
				flex-direction: column;
				gap: .3em;
			}

			.notice {
				opacity: .6;
				padding: .3em .1em;
			}
		`
	}

	protected override connected() {
		void this.refresh()
	}

	protected override updated(changed: PropertyValues) {
		if (changed.has('fateId') || changed.has('decreeId')) {
			void this.refresh()
		}
	}

	private async refresh() {
		if (this.fateId) {
			this.eventives = await core.declaratives.listEventives(this.fateId)
			this.attentives = []
		}
		else if (this.decreeId) {
			this.attentives = await core.declaratives.listAttentives(this.decreeId)
			this.eventives = []
		}
		else {
			this.eventives = []
			this.attentives = []
		}

		this.loading = false
	}

	protected override get template() {
		const empty = this.fateId ? this.eventives.length === 0 : this.attentives.length === 0

		return html`
			<div class='heading'>
				<p7t-icon icon='lucide:calendar-clock'></p7t-icon>
				<span>Agenda</span>
			</div>
			<div class='rows' @attentivechange=${(e: Event) => this.onAttentiveChange(e)}>
				${!empty ? nothing : html`
					<div class='notice'>${this.loading ? 'Loading…' : 'No occurrences yet.'}</div>
				`}
				${this.fateId
					? repeat(this.eventives, eventive => eventive.id, eventive => html`
						<p7t-eventive-item interactive .eventive=${eventive}></p7t-eventive-item>
					`)
					: repeat(this.attentives, attentive => attentive.id, attentive => html`
						<p7t-attentive-item interactive .attentive=${attentive}></p7t-attentive-item>
					`)}
			</div>
		`
	}

	/**
	 * An attentive toggled Done/Pending from a row here: re-read this list, and nudge the surfaces the change also
	 * touches — today's agenda (an occurrence can enter or leave it) and the owning decree's cached attentives.
	 */
	private onAttentiveChange(e: Event) {
		e.stopPropagation()
		void this.refresh()
		void core.repos.agenda.refresh()
		if (this.decreeId) {
			void core.repos.decrees.revalidateIfObserved(this.decreeId)
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-declarative-agenda': DeclarativeAgendaEditor
	}
}
