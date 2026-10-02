import { component, html, property, type HTMLTemplateResult } from "@a11d/lit"
import { OrbitUnit } from "@pleiades/sdk"
import type { IconName } from "../PleiadesIcon"
import { fuzzyFilter } from "./fuzzy"
import { SelectBase, type SelectOption } from "./SelectBase"

/** How one granularity — an {@link OrbitUnit} an occurrence's moment is real to — is named and drawn. */
export interface GranularityDescriptor {
	readonly label: string
	readonly icon: IconName
}

/** Every granularity, coarsest first, by its {@link OrbitUnit}. */
export const granularityDescriptors: Record<OrbitUnit, GranularityDescriptor> = {
	[OrbitUnit.Year]: { label: 'Year', icon: 'lucide:calendar-range' },
	[OrbitUnit.Month]: { label: 'Month', icon: 'lucide:calendar' },
	[OrbitUnit.Week]: { label: 'Week', icon: 'lucide:calendar-days' },
	[OrbitUnit.Day]: { label: 'Day', icon: 'lucide:sun' },
	[OrbitUnit.Hour]: { label: 'Hour', icon: 'lucide:clock' },
	[OrbitUnit.Minute]: { label: 'Minute', icon: 'lucide:timer' },
	[OrbitUnit.Second]: { label: 'Second', icon: 'lucide:hourglass' },
}

/** Every granularity, coarsest first. */
export const granularities: readonly OrbitUnit[] = [OrbitUnit.Year, OrbitUnit.Month, OrbitUnit.Week, OrbitUnit.Day, OrbitUnit.Hour, OrbitUnit.Minute, OrbitUnit.Second]

/**
 * Picks an Epoch's granularity — how much of its moment is real, from a whole year down to the second — as an inline
 * select over the seven {@link OrbitUnit}s, coarsest first, filtered by name as one types. A host whose value can
 * only take some of them narrows the choice through {@link units}.
 */
@component('p7t-granularity-select')
export class GranularitySelect extends SelectBase<OrbitUnit> {
	override placeholder = 'Granularity…'

	/** The granularities offered, in the order listed; all seven by default. */
	@property({ type: Array }) units: readonly OrbitUnit[] = granularities

	protected override async search(query: string): Promise<readonly SelectOption<OrbitUnit>[]> {
		return fuzzyFilter(query, this.units, unit => granularityDescriptors[unit].label).map(unit => ({
			key: `granularity:${unit}`,
			value: unit,
			template: this.chipTemplate(unit)
		}))
	}

	protected override renderValue(value: OrbitUnit): HTMLTemplateResult {
		return this.chipTemplate(value)
	}

	private chipTemplate(unit: OrbitUnit) {
		const descriptor = granularityDescriptors[unit]
		return html`<p7t-icon-item small .icon=${descriptor.icon}>${descriptor.label}</p7t-icon-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-granularity-select': GranularitySelect
	}
}
