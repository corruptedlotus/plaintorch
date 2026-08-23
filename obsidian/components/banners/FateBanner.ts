import { component, css, html, nothing, state } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Eventive, EventiveResolution, Fate, FateStatus, FateUpdate, PleiadeanDate } from '@pleiades/sdk'
import { App } from "obsidian"
import { core, IconName, ReactiveBinder, SelectFateStatusModal } from ".."

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

	protected get isSingleInstance() {
		return !this.entity!.orbit && !!this.entity!.date
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
				<p7t-date-view .date=${PleiadeanDate.fromDate(new Date(this.nextEventive.date))}></p7t-date-view>
				${!this.nextEventive.startTime ? nothing : html`
					<span class='time'>${formatTime(this.nextEventive.startTime)}</span>
				`}
			</div>
		`
	}

	protected override get actions() {
		// Single-instance fates show their datetime [range]; anything else exposes the
		// Orbit definition inline (editable), mirroring where a lorepage shows its date.
		if (this.isSingleInstance) {
			return html`
				<div class='date-span'>
					<p7t-date-view .date=${PleiadeanDate.fromDate(new Date(this.entity!.date!))}></p7t-date-view>
					${this.timeRangeTemplate}
				</div>
			`
		}

		return html`
			<div class='schedule'>
				<p7t-editable-orbit ${this.binder.bind('orbit')}></p7t-editable-orbit>
			</div>
		`
	}

	protected get timeRangeTemplate() {
		const start = this.entity!.startTime
		if (!start) return nothing
		const end = this.entity!.endTime
		return html`
			<span class='time'>${formatTime(start)}</span>
			${!end ? nothing : html`
				<p7t-icon icon='lucide:arrow-right'></p7t-icon>
				<span class='time'>${formatTime(end)}</span>
			`}
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

/** Trims a serialized TimeOnly ("HH:MM:SS") down to "HH:MM". */
function formatTime(time: string): string {
	return time.slice(0, 5)
}

function pad(value: number): string {
	return value.toString().padStart(2, '0')
}

declare global {
	interface HTMLTagNameMap {
		'p7t-fate-banner': FateBanner
	}
}
