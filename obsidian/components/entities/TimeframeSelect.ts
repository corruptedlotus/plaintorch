import { component, css, html, property, type HTMLTemplateResult } from "@a11d/lit"
import type { DirectiveTimeframeRecord } from "@pleiades/sdk"
import { core } from ".."
import { fuzzyFilter } from "../editing/fuzzy"
import { SelectBase, type SelectOption } from "../editing/SelectBase"

/**
 * Picks a timeframe to affine an activity to, as an inline select — the same list the affinity modal offers,
 * every lunar directive's timeframes, filtered by title or directive as one types, and led by a "no affinity"
 * choice so a set one can be cleared the same way. The list is fetched once per opening.
 */
@component('p7t-timeframe-select')
export class TimeframeSelect extends SelectBase<DirectiveTimeframeRecord> {
	@property({ type: Boolean, reflect: true }) thumbnail = false

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

	protected override async search(query: string): Promise<readonly SelectOption<DirectiveTimeframeRecord>[]> {
		this.timeframes ??= core.directives.listAllTimeframes()
		const matches = fuzzyFilter(query, await this.timeframes, timeframe => `${timeframe.title} ${timeframe.directiveTitle}`)

		return [
			{
				key: 'none',
				resolve: () => undefined,
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

	protected override renderValue(value: DirectiveTimeframeRecord | undefined): HTMLTemplateResult {
		return html`<p7t-timeframe-item class='face-item' ?thumbnail=${this.thumbnail} mode=${this.thumbnail ? 'icon' : 'named'} affinity .timeframe=${value}></p7t-timeframe-item>`
	}

	/** An unset affinity is a chip of its own ("No Affinity"), not a bare placeholder. */
	protected override renderEmpty(): HTMLTemplateResult {
		return this.renderValue(undefined)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframe-select': TimeframeSelect
	}
}
