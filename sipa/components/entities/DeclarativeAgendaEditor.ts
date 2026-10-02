import { Component, component, css, html, nothing, property, state } from '@a11d/lit'
import type { Attentive, DeclarativeCalendar, Eventive } from '@pleiades/sdk'
import { core, DerivedRef, type CreationRowCreated } from '..'
import { eventiveHorizonDays } from './EventiveCreationRow'

/**
 * One declarative's own agenda, listed in place beneath its banner — the fate's eventives or the decree's attentives.
 *
 * It is the system-wide agenda the briefing shows ({@link https PolarisAgenda}, `core.repos.agenda`) narrowed to this
 * one declarative, so it covers exactly the same range: a decree's attentives requiring attention (due within the
 * next 24 hours, or overdue and still pending) plus those done within the past hour, and a fate's pending eventives
 * over the next 7 days. It stays live the same way, revalidating on the feed. A fate is handed as {@link fateId}, a
 * decree as {@link decreeId}; exactly one is set.
 *
 * Each occurrence is a mini line — its state, its time, and whether it came from the orbit or was added by hand. A
 * fate's agenda can also add a manual eventive through the inline {@link EventiveCreationRow} at its foot, as the
 * Polaris card adds an activity.
 */
@component('p7t-declarative-agenda')
export class DeclarativeAgendaEditor extends Component {
	/** The fate whose eventives these are, when this agenda belongs to a fate. */
	@property() fateId = ''

	/** The decree whose attentives these are, when this agenda belongs to a decree. */
	@property() decreeId = ''

	/**
	 * The declarative's own calendar, when it names one. Each row reads its week, month or year granularity on the
	 * occurrence's own declarative when it carries one, and on this otherwise.
	 */
	@property({ type: Number }) calendar?: DeclarativeCalendar

	/** The declarative's own time zone, the one an eventive added here starts in; unset, it starts floating. */
	@property() timeZone?: string

	/** Whether the inline creation row for a manual eventive is open at the foot of the list. */
	@state() private creating = false

	private readonly agendaRef = new DerivedRef(this, core.repos.agenda)

	private get eventives(): readonly Eventive[] {
		const fateId = this.fateId
		return !fateId ? [] : (this.agendaRef.value?.eventives ?? []).filter(eventive => eventive.fateId === fateId)
	}

	private get attentives(): readonly Attentive[] {
		const decreeId = this.decreeId
		return !decreeId ? [] : (this.agendaRef.value?.attentives ?? []).filter(attentive => attentive.decreeId === decreeId)
	}

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
			}

			.notice {
				opacity: .6;
				padding: .3em .1em;
			}

			.add-button {
				align-self: flex-start;
			}
		`
	}

	protected override get template() {
		const rows = this.fateId ? this.eventives : this.attentives
		const loading = this.agendaRef.loading && !this.agendaRef.value

		return html`
			<div class='heading'>
				<p7t-icon icon='lucide:calendar-clock'></p7t-icon>
				<span>Agenda</span>
			</div>
			<div class='rows' @attentivechange=${(e: Event) => this.onAttentiveChange(e)}>
				${rows.length > 0 ? nothing : html`
					<div class='notice'>${loading ? 'Loading…' : this.emptyText}</div>
				`}
				${this.fateId
					? this.eventives.map(eventive => html`
						<p7t-eventive-item mini interactive .calendar=${this.calendar} .eventive=${eventive}></p7t-eventive-item>
					`)
					: this.attentives.map(attentive => html`
						<p7t-attentive-item mini interactive .calendar=${this.calendar} .attentive=${attentive}></p7t-attentive-item>
					`)}
			</div>
			${this.creationTemplate}
		`
	}

	/** What an empty agenda says — naming the range, since an occurrence further out is simply not listed yet. */
	private get emptyText() {
		return this.fateId
			? `Nothing in the next ${eventiveHorizonDays} days.`
			: 'Nothing requires attention in the next 24 hours.'
	}

	/** A fate's foot: the button that opens the creation row, or the row itself while it is open. */
	private get creationTemplate() {
		if (!this.fateId) {
			return nothing
		}

		return !this.creating
			? html`<p7t-button class='add-button' icon='lucide:plus' @click=${() => this.creating = true}>Add eventive</p7t-button>`
			: html`
				<p7t-eventive-creation-row
					.fateId=${this.fateId}
					.calendar=${this.calendar}
					.timeZone=${this.timeZone}
					@cancel=${() => this.creating = false}
					@created=${(e: CustomEvent<CreationRowCreated<Eventive>>) => this.onEventiveCreated(e)}>
				</p7t-eventive-creation-row>
			`
	}

	/** A committed row: the agenda re-reads itself through the row's own revalidation; the row stays only to make another. */
	private onEventiveCreated(e: CustomEvent<CreationRowCreated<Eventive>>) {
		e.stopPropagation()
		if (e.detail.mode !== 'again') {
			this.creating = false
		}
	}

	/**
	 * An attentive toggled Done/Pending from a row here: re-read the agenda (an occurrence can enter or leave it), and
	 * nudge the owning decree's cached attentives. A change made through the occurrence editor revalidates the agenda
	 * itself.
	 */
	private onAttentiveChange(e: Event) {
		e.stopPropagation()
		void this.agendaRef.refresh()
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
