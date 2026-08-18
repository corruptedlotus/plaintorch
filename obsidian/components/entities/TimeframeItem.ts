import { component, css, html, nothing, property } from '@a11d/lit'
import { ObjectiveCollege, TimeframeInclusion, type MediaReference } from '@pleiades/sdk'
import { humanizeOrbit } from 'orbits'
import { getApp, resolveMediaIcon } from '..'
import { InfoItem } from '../design/InfoItem'
import { collegeDescriptorOf } from './collegeDescriptors'

/** The subset of a timeframe (or a directive-timeframe record) the chip reads. */
export interface TimeframeLike {
	title: string
	startTime?: string
	endTime?: string
	orbit?: string
	icon?: string
	iconMedia?: MediaReference
	autoInclusion?: TimeframeInclusion
	autoInclusionCollege?: ObjectiveCollege
}

/** 'HH:MM[:SS]' → 'HH:MM'. */
const hhmm = (time: string | undefined): string => (time ?? '').slice(0, 5)

/**
 * A timeframe (PEP100) drawn one unified way — its icon, and (in `named` mode) its title. The built-in tooltip is
 * the timeframe's detail: its window, the cycles it scopes to (an Orbit, or every cycle), and the college it
 * auto-includes. `icon` mode is the compact form an affined executive shows in place of its Celestron.
 */
@component('p7t-timeframe-item')
export class TimeframeItem extends InfoItem {
	@property({ type: Object }) timeframe?: TimeframeLike
	@property() mode: 'icon' | 'named' = 'named'

	static override get styles() {
		return css`
			${super.styles}

			.named {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				font-weight: 400;
			}

			.named p7t-icon,
			.icon-only {
				width: 20px;
				height: 20px;
			}

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

	private get glyph(): string {
		return resolveMediaIcon(this.timeframe?.iconMedia, getApp(), 'lucide:clock')
	}

	protected override get content() {
		const timeframe = this.timeframe
		if (!timeframe) {
			return nothing
		}

		if (this.mode === 'icon') {
			return html`<p7t-icon class='icon-only' .icon=${this.glyph}></p7t-icon>`
		}

		return html`
			<span class='named'>
				<p7t-icon .icon=${this.glyph}></p7t-icon>
				<span>${timeframe.title}</span>
			</span>
		`
	}

	protected override get tooltip() {
		const timeframe = this.timeframe
		if (!timeframe) {
			return nothing
		}

		const window = timeframe.startTime && timeframe.endTime
			? `${hhmm(timeframe.startTime)} – ${hhmm(timeframe.endTime)}`
			: undefined
		// No Orbit means the timeframe applies to every Polaris cycle (PEP100).
		const scope = timeframe.orbit ? (humanizeOrbit(timeframe.orbit).text || timeframe.orbit) : 'Every cycle'
		const college = timeframe.autoInclusion === TimeframeInclusion.College && timeframe.autoInclusionCollege !== undefined
			? collegeDescriptorOf(timeframe.autoInclusionCollege)
			: undefined

		return html`
			<div class='details'>
				<div class='title'>${timeframe.title}</div>
				${!window ? nothing : html`<div class='row'><p7t-icon icon='lucide:clock'></p7t-icon><span>${window}</span></div>`}
				<div class='row'><p7t-icon icon='lucide:repeat'></p7t-icon><span>${scope}</span></div>
				${!college ? nothing : html`<div class='row'><p7t-icon icon=${college.icon}></p7t-icon><span>Includes ${college.name}</span></div>`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframe-item': TimeframeItem
	}
}
