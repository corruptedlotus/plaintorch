import { component, html, nothing, property, type HTMLTemplateResult } from '@a11d/lit'
import type { Activity } from '@pleiades/sdk'
import { resolveMediaIcon } from '..'
import { InfoItem } from '../design/InfoItem'

/**
 * An activity — an objective or a decree — as a one-line chip: its parent directive's icon (the directive's own
 * media, or the asteroid glyph for one outside any directive), its title, and its chips: the kind, the college,
 * and the Celestron it is worth. The dense form a select's list and face want, where the full item row would
 * be three lines of chrome around one line of information.
 *
 * It is nothing without its chips, so it always shows them rather than waiting on `chipped`. The base's `chips`
 * slot follows its own tags, so a consumer can add one by hand — a select marks an unavailable row "In cycle".
 */
@component('p7t-mini-activity-item')
export class MiniActivityItem extends InfoItem {
	@property({ type: Object }) activity?: Activity

	private get incentive() {
		return this.activity?.kind === 'decree' ? this.activity.decree : this.activity?.objective
	}

	protected override get bulletIcon(): string {
		const directive = this.incentive?.directive
		return directive ? resolveMediaIcon(directive.iconMedia, directive.isLunar ? 'directive-lunar' : 'directive') : 'lucide:astroid'
	}

	protected override get bulletText() {
		return this.activity?.title ?? ''
	}

	protected override get showChips(): boolean {
		return true
	}

	protected override get chipsTemplate(): HTMLTemplateResult {
		const activity = this.activity
		if (!activity) {
			return html``
		}

		const decree = activity.kind === 'decree'
		const college = decree ? activity.decree?.college : activity.objective?.college
		const celestron = decree ? activity.decree?.activeCelestron : activity.objective?.celestronValue
		return html`
			${decree ? html`<span data-accent>Decree</span>` : nothing}
			${college === undefined ? html`` : html`<p7t-college-item small mode='named' .college=${college}></p7t-college-item>`}
			${celestron === undefined ? html`` : html`<p7t-celestron-item small .value=${celestron}></p7t-celestron-item>`}
			<slot name='chips'></slot>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-mini-activity-item': MiniActivityItem
	}
}
