import { component, css, html, property, type HTMLTemplateResult } from "@a11d/lit"
import type { DirectiveTimeframeRecord } from "@pleiades/sdk"
import { core } from ".."
import { fuzzyFilter } from "../editing/fuzzy"
import { SelectBase, type SelectOption } from "../editing/SelectBase"
import type { TimeframeChoice } from "../editing/SelectTimeframeModal"

/**
 * Picks a timeframe to affine an activity to, as an inline select — the same list the affinity modal offers,
 * every lunar directive's timeframes, filtered by title or directive as one types, and led by an explicit "no
 * affinity" choice (`null`) so a set one can be cleared the same way. The list is fetched once per opening.
 *
 * The value is a {@link TimeframeChoice}: a timeframe record, or `null` for an explicit none. With {@link auto} set,
 * an unset value (`undefined`) means **Auto** (PEP100 patch 2) — the core assigns the affinity — and the list leads
 * with an Auto choice that returns the field to it.
 */
@component('p7t-timeframe-select')
export class TimeframeSelect extends SelectBase<TimeframeChoice> {
	/** Draws the field as the chip's glyph alone (its `icon` mode), for a compact creation row. */
	@property({ type: Boolean, reflect: true }) thumbnail = false

	/**
	 * Offers **Auto** (PEP100 patch 2): the list leads with an Auto choice and an unset (`undefined`) value is drawn
	 * as the Auto face, standing for "let the core assign it" — the incentive's directive availability, else its
	 * college. Off by default, so the select stays a plain timeframe picker. Note that the inherited clear of a
	 * `nullable` field commits `undefined`, which reads as Auto here, not as none.
	 */
	@property({ type: Boolean, reflect: true }) auto = false

	static override get styles() {
		return css`
			${super.styles}

			:host([thumbnail]) {
				min-width: 0 !important;

				& .field {
					justify-content: center !important;
				}
			}
		`
	}

	override placeholder = 'Affinity…'

	private timeframes?: Promise<DirectiveTimeframeRecord[]>

	public override open() {
		this.timeframes = undefined
		super.open()
	}

	/**
	 * The options for a query: the Auto choice first when {@link auto} is set (it resolves `undefined`), then the
	 * explicit none (`null`), then the timeframes whose title or directive fuzzily match.
	 */
	protected override async search(query: string): Promise<readonly SelectOption<TimeframeChoice>[]> {
		this.timeframes ??= core.directives.listAllTimeframes()
		const matches = fuzzyFilter(query, await this.timeframes, timeframe => `${timeframe.title} ${timeframe.directiveTitle}`)

		const autoOption: SelectOption<TimeframeChoice> = {
			key: 'auto',
			resolve: () => undefined,
			template: html`<p7t-timeframe-item small affinity auto></p7t-timeframe-item>`
		}

		return [
			...(this.auto ? [autoOption] : []),
			{
				key: 'none',
				value: null,
				template: html`<p7t-timeframe-item small affinity></p7t-timeframe-item>`
			},
			...matches.map(timeframe => ({
				key: `timeframe:${timeframe.id}`,
				value: timeframe,
				// `chipped` adds the owning directive as the timeframe's chip — which lunar directive it belongs to.
				template: html`<p7t-timeframe-item small chipped .timeframe=${timeframe}></p7t-timeframe-item>`
			}))
		]
	}

	/**
	 * A chosen timeframe as its affinity chip; `null` (an explicit none) reads "No Affinity", and `undefined` reads
	 * "Auto Affinity" when {@link auto} is set, else "No Affinity".
	 */
	protected override renderValue(value: TimeframeChoice | undefined): HTMLTemplateResult {
		return html`<p7t-timeframe-item class='face-item' ?thumbnail=${this.thumbnail} mode=${this.thumbnail ? 'icon' : 'named'} affinity ?auto=${this.auto && value === undefined} .timeframe=${value ?? undefined}></p7t-timeframe-item>`
	}

	/** An unset affinity is a chip of its own — "Auto Affinity" with {@link auto}, else "No Affinity" — not a bare placeholder. */
	protected override renderEmpty(): HTMLTemplateResult {
		return this.renderValue(undefined)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframe-select': TimeframeSelect
	}
}
