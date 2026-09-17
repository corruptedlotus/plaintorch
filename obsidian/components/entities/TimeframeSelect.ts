import { component, html, type HTMLTemplateResult } from "@a11d/lit"
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
				template: html`<p7t-timeframe-item small .timeframe=${timeframe}></p7t-timeframe-item> <span class='placeholder'>· ${timeframe.directiveTitle}</span>`
			}))
		]
	}

	protected override renderValue(value: DirectiveTimeframeRecord): HTMLTemplateResult {
		return html`<p7t-timeframe-item small affinity .timeframe=${value}></p7t-timeframe-item>`
	}

	/** An unset affinity is a chip of its own ("No Affinity"), not a bare placeholder. */
	protected override renderEmpty(): HTMLTemplateResult {
		return html`<p7t-timeframe-item small affinity></p7t-timeframe-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframe-select': TimeframeSelect
	}
}
