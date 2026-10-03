import { component, css, html, state } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { DependencyEndpointKind, Eventive, EventiveResolution, Fate, FateStatus, FateUpdate } from '@pleiades/sdk'
import { core, fateScheduleUpdate, IconName, followRenamedNote, ReactiveBinder, SelectStatusModal, fateStatusDescriptors } from ".."
import { declarativeResolutionStyles, declarativeResolutionTemplate, type DeclarativeResolutionUpdate } from './declarativeResolution'
import { occurrenceTime } from '../entities/occurrenceTime'
import type { ScheduleValue } from "../editing/EditableSchedule"

/**
 * Banner for a Fate declarative (PEP100). Fates are event-like: they show their Orbit
 * definition — the same place a lorepage shows its date — when they recur, or their
 * datetime [range] when they are single-instance, with the next materialized eventive
 * surfaced above.
 */
@component('p7t-fate-banner')
export class FateBanner extends EntityBanner<Fate> {
	// TODO(icons): no dedicated 'fate' icon exists yet; 'eventive' stands in for now.
	override icon: IconName = 'fate'

	@state() nextEventive?: Eventive

	protected binder = new ReactiveBinder<Fate>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			const update: FateUpdate = {}
			switch (keyPath) {
				case 'status':
					update.status = entity.status
					break
				case 'orbit':
					// Empty string clears the schedule server-side; undefined would be a no-op.
					update.orbit = entity.orbit ?? ''
					break
				case 'title':
					update.title = entity.title
					break
				default:
					return
			}

			const saved = await this.commitEntityEdit(async () => await core.declaratives.updateFate(entity.id, update))
			if (!saved) {
				return
			}

			await this.loadNextEventive(entity.id)

			if (keyPath === 'title') {
				await followRenamedNote(entity.id)
			}
		}
	})

	protected override readonly entityTypeName = 'Fate' as const

	/** Takes part in dependencies as a fate endpoint, so the banner carries its dependency route. */
	protected override get dependencyEndpoint() {
		return this.entity ? { kind: DependencyEndpointKind.Fate, id: this.entity.id } : undefined
	}

	protected override async loadRelated() {
		if (this.puck) {
			await this.loadNextEventive(this.puck)
		}
	}

	private async loadNextEventive(fateId: string) {
		const eventives = await core.declaratives.listEventives(fateId)
		this.nextEventive = pickNextEventive(eventives)
	}

	/**
	 * Persists a schedule edited through the schedule's bubble. A fate is orbit-only (PEP111): a recurrence passes
	 * through, and a one-shot `Z{…}` literal comes with the calendar it was written on, sent along so the core reads the
	 * literal on it.
	 */
	private async onScheduleChange(value: ScheduleValue) {
		const entity = this.entity!
		const update: FateUpdate = fateScheduleUpdate(value)

		this.beginEntityEdit()
		const saved = await this.commitEntityEdit(async () => await core.declaratives.updateFate(entity.id, update))
		if (saved) {
			await this.loadNextEventive(entity.id)
		}
	}

	static override get styles() {
		return css`
			${super.styles}

			${declarativeResolutionStyles}

			:host {
				padding-inline: 1.2em;
			}

			:host::part(sub-heading) {
				font-weight: 300;
				font-size: .9em;
				margin-top: -.2em;
				opacity: 1;
			}

			p7t-status-item::part(icon) {
				height: 1.4em;
			}

			.next-eventive {
				display: flex;
				align-items: center;
				gap: .5em;
				font-weight: 300;
			}

			.next-eventive .label {
				font-size: .7em;
				text-transform: uppercase;
				letter-spacing: .08em;
				opacity: .6;
			}

			.schedule,
			.date-span {
				display: flex;
				align-items: center;
				gap: .5em;
				font-weight: 300;
				opacity: .85;
			}

			.schedule p7t-editable-orbit {
				font-size: 1em;
			}

			.time {
				font-variant-numeric: tabular-nums;
				opacity: .8;
			}
		`
	}

	protected override get secondary() {
		return html`
			<p7t-editable-hierarchy .entity=${this.entity}></p7t-editable-hierarchy>
		`
	}

	protected override get info() {
		const next = this.nextEventive
		// A zoned eventive reads on this device's clock, its own wall time in the tooltip.
		const time = next ? occurrenceTime(next.epoch) : undefined
		return html`
			${!next || !time ? html`` : html`
				<div class='next-eventive'>
					<span class='label'>Next</span>
					<p7t-datetime-view
						.date=${time.date}
						.time=${time.time}
						.anchor=${time.anchor?.label}
						.granularity=${next.epoch.granularity}
						.calendar=${this.entity!.calendar}>
					</p7t-datetime-view>
				</div>
			`}
			${declarativeResolutionTemplate(this.entity!, update => void this.saveResolution(update))}
		`
	}

	/** Persists a calendar or time zone chosen in the resolution row; a one-off fate's literal is rewritten by the core. */
	private async saveResolution(update: DeclarativeResolutionUpdate) {
		const entity = this.entity!
		this.beginEntityEdit()
		const saved = await this.commitEntityEdit(async () => await core.declaratives.updateFate(entity.id, update))
		if (saved) {
			await this.loadNextEventive(entity.id)
		}
	}

	protected override get actions() {
		// The fate's orbit, edited in a bubble as notation or as a one-shot moment written as a `Z{…}` literal (a fate is
		// orbit-only, PEP111); a stored one-shot opens on its moment.
		return html`
			<p7t-editable-schedule
				.calendar=${this.entity!.calendar}
				.orbit=${this.entity!.orbit}
				@schedulechange=${(e: CustomEvent<ScheduleValue>) => void this.onScheduleChange(e.detail)}>
			</p7t-editable-schedule>
		`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get preHeadingTemplate() {
		return html`<span>Pleiades Fate</span>`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-editable .doEdit=${() => SelectStatusModal.prompt(fateStatusDescriptors)} ${this.binder.bind('status')}>
				<p7t-status-item
					.status=${FateStatus[this.entity!.status] as keyof typeof FateStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}
}

/** Earliest still-pending eventive on or after today, as they fall on this device's clock; undefined when none is upcoming. */
function pickNextEventive(eventives: Eventive[]): Eventive | undefined {
	const today = todayKey()
	return eventives
		.map(eventive => ({ eventive, time: occurrenceTime(eventive.epoch) }))
		.filter(({ eventive, time }) => eventive.resolution === EventiveResolution.Pending && time.date >= today)
		.sort((a, b) => a.time.date.localeCompare(b.time.date) || (a.time.time ?? '').localeCompare(b.time.time ?? ''))[0]
		?.eventive
}

function todayKey(): string {
	const now = new Date()
	return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

function pad(value: number): string {
	return value.toString().padStart(2, '0')
}

declare global {
	interface HTMLTagNameMap {
		'p7t-fate-banner': FateBanner
	}
}
