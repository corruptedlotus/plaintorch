import { Component, component, css, html, nothing, property } from '@a11d/lit'
import { TimeframeInclusion } from '@pleiades/sdk'
import { humanizeOrbit } from '../../orbits'
import { CalendarRef } from '../data/CalendarRef'
import { inclusionDescriptors } from './inclusionDescriptors'
import type { TimeframeLike } from './TimeframeItem'
import '../PleiadesIcon'

/** 'HH:MM[:SS]' → 'HH:MM'. */
const hhmm = (time: string | undefined): string => (time ?? '').slice(0, 5)

/**
 * The full timeframe detail drawn in {@link TimeframeItem}'s tooltip — its window, the cycles it scopes to (an
 * Orbit, or every cycle), whether it is exclusive, and how it auto-includes: the colleges it affines in College mode,
 * or directive availability in Availability mode (PEP100 patch 2). The Orbit is read on the vault's preferred calendar,
 * the one the core reads every timeframe orbit on. A self-contained element (its own shadow root and
 * styles) so it renders identically wherever the tooltip system places it, independent of any host's shadow scope.
 */
@component('p7t-timeframe-details')
export class TimeframeDetails extends Component {
	@property({ type: Object }) timeframe?: TimeframeLike

	/** The calendar the Orbit is read on: a timeframe names none, so the vault's preferred one. */
	private readonly calendars = new CalendarRef(this, () => undefined, () => !!this.timeframe?.orbit)

	static override get styles() {
		return css`
			:host { display: contents; }

			.details {
				display: flex;
				flex-direction: column;
				gap: .25em;
				min-width: 12em;
			}

			.details .title {
				font-weight: 600;
			}

			.details .row {
				display: flex;
				align-items: center;
				gap: .5ch;
				opacity: .85;
			}

			.details .row p7t-icon {
				width: 1.1em;
				height: 1.1em;
				opacity: .7;
			}
		`
	}

	protected override get template() {
		const timeframe = this.timeframe
		if (!timeframe) {
			return html``
		}

		const window = timeframe.startTime && timeframe.endTime
			? `${hhmm(timeframe.startTime)} – ${hhmm(timeframe.endTime)}`
			: undefined
		// No Orbit means the timeframe applies to every Polaris cycle (PEP100).
		const scope = timeframe.orbit ? (humanizeOrbit(timeframe.orbit, false, this.calendars.orbitCalendar).text || timeframe.orbit) : 'Every cycle'
		const colleges = timeframe.autoInclusion === TimeframeInclusion.College
			? timeframe.autoInclusionColleges ?? []
			: []

		return html`
			<div class='details'>
				<div class='title'>${timeframe.title}</div>
				${!window ? nothing : html`<div class='row'><p7t-icon icon='lucide:clock'></p7t-icon><span>${window}</span></div>`}
				<div class='row'><p7t-icon icon='lucide:repeat'></p7t-icon><span>${scope}</span></div>
				${!timeframe.exclusive ? nothing : html`<div class='row'><p7t-icon icon='lucide:lock'></p7t-icon><span>Exclusive</span></div>`}
				${timeframe.autoInclusion !== TimeframeInclusion.Availability ? nothing : html`
					<div class='row'><p7t-icon icon=${inclusionDescriptors.Availability.icon}></p7t-icon><span>${inclusionDescriptors.Availability.fullName}</span></div>
				`}
				${colleges.length === 0 ? nothing : html`
					<div class='row'>
						<p7t-icon icon='lucide:layers'></p7t-icon>
						<p7t-icon-item chipped>
							Affined to
							${colleges.map(college => html`
								<p7t-college-item small slot='chips' .college=${college}></p7t-college-item>
							`)}
						</p7t-icon-item>
					</div>
				`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframe-details': TimeframeDetails
	}
}
