import { component, css, html, nothing, property } from '@a11d/lit'
import { ObjectiveCollege, TimeframeInclusion, type Directive, type LunarDirectiveStatus, type MediaReference } from '@pleiades/sdk'
import { IconName } from 'components/PleiadesIcon'
import { core, getApp, resolveMediaIcon } from '..'
import { InfoItem } from '../design/InfoItem'
import './TimeframeDetails'
import './DirectiveItem'

/** The subset of a timeframe (or a directive-timeframe record) the chip reads. */
export interface TimeframeLike {
	title: string
	/** The owning lunar directive — every timeframe knows its id; a record also carries its title and state. */
	directiveId?: string
	directiveTitle?: string
	directiveStatus?: LunarDirectiveStatus
	startTime?: string
	endTime?: string
	orbit?: string
	icon?: string
	iconMedia?: MediaReference
	autoInclusion?: TimeframeInclusion
	autoInclusionColleges?: ObjectiveCollege[]
}

/**
 * A timeframe (PEP100) drawn one unified way — its icon, and (in `named` mode) its title. The built-in tooltip is
 * the timeframe's detail (see {@link TimeframeDetails}): its window, the cycles it scopes to (an Orbit, or every
 * cycle), and the college it auto-includes. `icon` mode is the compact form an affined executive shows in place of
 * its Celestron.
 */
@component('p7t-timeframe-item')
export class TimeframeItem extends InfoItem {
	@property({ type: Object }) timeframe?: TimeframeLike
	@property() mode: 'icon' | 'named' = 'named'

	/**
	 * Draws the chip as an executive's **affinity** rather than a timeframe in the abstract: an empty one reads as
	 * "No Affinity" behind a polaris glyph, a set one suffixes "Affinity" to the name, and the tooltip drops the
	 * scheduling detail (window, scope, college) — an affinity is only *which* timeframe, not its mechanics.
	 */
	@property({ type: Boolean }) affinity = false

	static override get styles() {
		return css`
			${super.styles}

			.info-bullet {
				font-weight: 400;
			}
		`
	}

	private get glyph(): string {
		return resolveMediaIcon(this.timeframe?.iconMedia, getApp(), 'lucide:clock')
	}

	/** The timeframe's media companion (or the polaris glyph for an unset affinity), drawn through the base icon slot. */
	protected override get bulletIcon(): IconName | (string & {}) | undefined {
		if (!this.timeframe) {
			return this.affinity ? 'polaris' : undefined
		}

		return this.glyph
	}

	protected override get bulletText() {
		const timeframe = this.timeframe
		if (!timeframe) {
			return 'No Affinity'
		}

		return this.affinity ? `${timeframe.title} Affinity` : timeframe.title
	}

	/**
	 * The timeframe's chip is the lunar directive that owns it, as a {@link DirectiveItem} — shown only where a
	 * consumer asks with `chipped` (a select's list, where two directives may each have a "Morning").
	 *
	 * The live directive is preferred, for its real state in the chip's tooltip; a timeframe record carries its
	 * owner's title, which is enough to stand in until the directive is loaded. A bare timeframe that knows only
	 * its owner's id, with the directive not loaded, has nothing to name and draws no chip.
	 */
	protected override get chipsTemplate() {
		const timeframe = this.timeframe
		const directiveId = timeframe?.directiveId
		const directive = (directiveId ? core.repos.lunarDirectives.peek(directiveId) : undefined)
			?? (timeframe?.directiveTitle ? { id: directiveId ?? '', title: timeframe.directiveTitle, status: timeframe.directiveStatus, isLunar: true } as unknown as Directive : undefined)
		return directive ? html`<p7t-directive-item small .directive=${directive}></p7t-directive-item>` : html``
	}

	/** `icon` mode is the glyph alone — an affined executive's compact form; the label moves to the tooltip. */
	protected override get textHidden(): boolean {
		return this.mode === 'icon'
	}

	protected override get content() {
		// Only a non-affinity empty timeframe steps outside the icon-text layout, for the null glyph (or nothing);
		// an empty affinity keeps the layout to read "No Affinity" behind the polaris glyph.
		if (!this.timeframe && !this.affinity) {
			return this.nullable ? this.nullGlyphTemplate : nothing
		}

		return super.content
	}

	protected override get tooltip() {
		const timeframe = this.timeframe

		// An affinity is just which timeframe it is: its name suffixed with "Affinity", or "No Affinity" when unset.
		if (this.affinity) {
			return timeframe ? `${timeframe.title} Affinity` : 'No Affinity'
		}

		// The full timeframe detail is a self-contained element so it survives the tooltip system's shadow isolation.
		return timeframe
			? html`<p7t-timeframe-details .timeframe=${timeframe}></p7t-timeframe-details>`
			: nothing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframe-item': TimeframeItem
	}
}
