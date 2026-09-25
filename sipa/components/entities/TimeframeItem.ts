import { component, css, html, nothing, property } from '@a11d/lit'
import { ObjectiveCollege, TimeframeInclusion, type Directive, type LunarDirectiveStatus, type MediaReference } from '@pleiades/sdk'
import { IconName } from '../PleiadesIcon'
import { core, resolveMediaIcon } from '..'
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
	/** Whether the timeframe, while active, suppresses the non-exclusive active ones (PEP100 patch 2). */
	exclusive?: boolean
}

/** The auto-affinity tooltip: what the core does with an affinity left on Auto (PEP100 patch 2). */
const autoAffinityTooltip = 'Assigned automatically: the activity\'s directive availability, else its college'

/**
 * A timeframe (PEP100) drawn one unified way — its icon, and (in `named` mode) its title. The built-in tooltip is
 * the timeframe's detail (see {@link TimeframeDetails}): its window, the cycles it scopes to (an Orbit, or every
 * cycle), whether it is exclusive, and how it auto-includes. `icon` mode is the compact form an affined executive
 * shows in place of its Celestron.
 *
 * Two flavours draw the chip as a *role* a timeframe plays rather than the timeframe in the abstract: {@link affinity}
 * (an executive's preferred timeframe) and {@link availability} (a directive's availability, PEP100 patch 2).
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

	/**
	 * Draws the chip as a directive's **availability** (PEP100 patch 2), mirroring {@link affinity}: an empty one reads
	 * as "No Availability", a set one suffixes "Availability" to the name, and the tooltip is only the name.
	 */
	@property({ type: Boolean }) availability = false

	/**
	 * With {@link affinity} and no timeframe, reads as an **automatic** affinity (PEP100 patch 2) — "Auto Affinity"
	 * behind its own glyph — rather than none: the core assigns it from the activity's directive availability, else
	 * its college. Ignored once a timeframe is set.
	 */
	@property({ type: Boolean }) auto = false

	/** The role the chip is drawn in, if any: availability wins over affinity should a consumer set both. */
	private get flavour(): 'affinity' | 'availability' | undefined {
		return this.availability ? 'availability' : this.affinity ? 'affinity' : undefined
	}

	/** Whether the chip is the Auto face: an unset affinity the core will assign. */
	private get isAuto() {
		return !this.timeframe && this.flavour === 'affinity' && this.auto
	}

	static override get styles() {
		return css`
			${super.styles}

			.info-bullet {
				font-weight: 400;
			}
		`
	}

	private get glyph(): string {
		return resolveMediaIcon(this.timeframe?.iconMedia, 'lucide:clock')
	}

	/**
	 * The timeframe's media companion, drawn through the base icon slot. Unset, a flavoured chip keeps a glyph of its
	 * own: polaris for no affinity, a wand for the Auto affinity, a crossed calendar for no availability.
	 */
	protected override get bulletIcon(): IconName | (string & {}) | undefined {
		if (!this.timeframe) {
			switch (this.flavour) {
				case 'affinity':
					return this.isAuto ? 'lucide:wand-sparkles' : 'polaris'
				case 'availability':
					return 'lucide:calendar-off'
				default:
					return undefined
			}
		}

		return this.glyph
	}

	/** The chip's label: the flavoured name when drawn as an affinity or availability, else the timeframe's title. */
	protected override get bulletText() {
		return this.flavouredName ?? this.timeframe?.title ?? ''
	}

	/** The name a flavoured chip reads as ("Morning Affinity", "No Availability", "Auto Affinity"); undefined when unflavoured. */
	private get flavouredName() {
		const timeframe = this.timeframe
		switch (this.flavour) {
			case 'affinity':
				return timeframe ? `${timeframe.title} Affinity` : this.isAuto ? 'Auto Affinity' : 'No Affinity'
			case 'availability':
				return timeframe ? `${timeframe.title} Availability` : 'No Availability'
			default:
				return undefined
		}
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

	/** The chip's body: the icon-text layout, or the null glyph (or nothing) for an unflavoured chip with no timeframe. */
	protected override get content() {
		// Only an unflavoured empty timeframe steps outside the icon-text layout, for the null glyph (or nothing);
		// an empty affinity or availability keeps the layout to read "No Affinity"/"No Availability" behind its glyph.
		if (!this.timeframe && !this.flavour) {
			return this.nullable ? this.nullGlyphTemplate : nothing
		}

		return super.content
	}

	/**
	 * The Auto explanation for the Auto face, the flavoured name for an affinity or availability chip, and otherwise
	 * the full {@link TimeframeDetails}.
	 */
	protected override get tooltip() {
		const timeframe = this.timeframe

		if (this.isAuto) {
			return autoAffinityTooltip
		}

		// A flavoured chip is just which timeframe it is: its name suffixed with its role, or "No …" when unset.
		const flavouredName = this.flavouredName
		if (flavouredName !== undefined) {
			return flavouredName
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
