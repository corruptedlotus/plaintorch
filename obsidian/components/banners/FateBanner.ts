import { component, css, html, state } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Eventive, EventiveResolution, Fate, FateStatus, FateUpdate } from '@pleiades/sdk'
import { App } from "obsidian"
import { core, IconName, ReactiveBinder, SelectFateStatusModal } from ".."
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
	override icon: IconName = 'eventive'

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
				await this.revealAssociatedNote(entity.id)
			}
		}
	})

	protected override readonly entityTypeName = 'Fate' as const

	protected override async loadRelated() {
		if (this.puck) {
			await this.loadNextEventive(this.puck)
		}
	}

	private async loadNextEventive(fateId: string) {
		const eventives = await core.declaratives.listEventives(fateId)
		this.nextEventive = pickNextEventive(eventives)
	}

	private async revealAssociatedNote(fateId: string) {
		const existence = await core.repos.entityResolution.refresh(fateId)
		const app = (window as any).app as App
		if (!existence?.associatedNote
			|| app.workspace.activeEditor?.file?.path === existence.associatedNote) return

		const file = app.vault.getFileByPath(existence.associatedNote)!
		app.workspace.getLeaf(true).openFile(file)
	}

	/**
	 * Persists a schedule edited through the unified control. Orbit and date are mutually exclusive, but the core
	 * interceptor clears whichever shape this update does not set, so only the chosen one is sent. The event
	 * window's end is carried only in the one-off shape (the control runs in range mode here).
	 */
	private async onScheduleChange(value: ScheduleValue) {
		const entity = this.entity!
		const update: FateUpdate = value.mode === 'orbit'
			? { orbit: value.orbit ?? '' }
			: { date: value.date, startTime: value.time, endTime: value.endTime }

		this.beginEntityEdit()
		const saved = await this.commitEntityEdit(async () => await core.declaratives.updateFate(entity.id, update))
		if (saved) {
			await this.loadNextEventive(entity.id)
		}
	}

	static override get styles() {
		return css`
			${super.styles}

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
			<p7t-directive-item .directive=${this.entity!.directive}></p7t-directive-item>
		`
	}

	protected override get info() {
		if (!this.nextEventive) return html``
		return html`
			<div class='next-eventive'>
				<span class='label'>Next</span>
				<p7t-datetime-view .date=${this.nextEventive.date} .time=${this.nextEventive.startTime}></p7t-datetime-view>
			</div>
		`
	}

	protected override get actions() {
		// One unified control for both shapes: a recurring orbit or a one-off date with an event-time range.
		return html`
			<p7t-editable-schedule
				range
				.orbit=${this.entity!.orbit}
				.date=${this.entity!.date}
				.time=${this.entity!.startTime}
				.endTime=${this.entity!.endTime}
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
			<p7t-editable .doEdit=${SelectFateStatusModal.prompt} ${this.binder.bind('status')}>
				<p7t-status-item
					.status=${FateStatus[this.entity!.status] as keyof typeof FateStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}
}

/** Earliest still-pending eventive on or after today; undefined when none is upcoming. */
function pickNextEventive(eventives: Eventive[]): Eventive | undefined {
	const today = todayKey()
	return eventives
		.filter(eventive => eventive.resolution === EventiveResolution.Pending && eventive.date >= today)
		.sort((a, b) => a.date.localeCompare(b.date) || (a.startTime ?? '').localeCompare(b.startTime ?? ''))[0]
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
