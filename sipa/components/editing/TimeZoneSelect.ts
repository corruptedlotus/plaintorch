import { component, html, type HTMLTemplateResult } from "@a11d/lit"
import { fuzzyFilter } from "./fuzzy"
import { SelectBase, type SelectOption } from "./SelectBase"

/** How many zones a search lists at most: enough to scan, few enough to stay quick among several hundred. */
const listedZones = 50

/** Every IANA zone this engine knows. `Intl.supportedValuesOf` is newer than the library the SIPA types build against. */
function supportedZones(): readonly string[] {
	const intl = Intl as typeof Intl & { supportedValuesOf?(key: 'timeZone'): string[] }
	return intl.supportedValuesOf?.('timeZone') ?? []
}

/** A zone's city, as people name it: the last part of its IANA id, with spaces ("Asia/Tehran" → "Tehran"). */
export function zoneCity(zone: string): string {
	return zone.slice(zone.lastIndexOf('/') + 1).replace(/_/g, ' ')
}

/** The zone this device keeps its clock in. */
function systemZone(): string {
	return Intl.DateTimeFormat().resolvedOptions().timeZone
}

/** A zone's offset from UTC right now, as the language writes it ("GMT+2"), or nothing when the engine cannot say. */
function offsetOf(zone: string): string {
	try {
		return new Intl.DateTimeFormat(undefined, { timeZone: zone, timeZoneName: 'shortOffset' })
			.formatToParts(new Date())
			.find(part => part.type === 'timeZoneName')?.value ?? ''
	}
	catch {
		return ''
	}
}

/**
 * Picks the time zone an Epoch's moment is anchored in, as an inline select. Unset (`undefined`) is **floating**: the
 * moment is wall-clock time wherever one is, as an Epoch with no zone reads. The list leads with that choice and this
 * device's own zone, then every IANA zone matching the search (by city or region), each with its offset.
 */
@component('p7t-time-zone-select')
export class TimeZoneSelect extends SelectBase<string> {
	override placeholder = 'Time zone…'

	protected override async search(query: string): Promise<readonly SelectOption<string>[]> {
		const own = systemZone()
		const zones = fuzzyFilter(query, supportedZones().filter(zone => zone !== own), zone => zone.replace(/_/g, ' '))
			.slice(0, listedZones)
		const floating: SelectOption<string> = { key: 'floating', resolve: () => undefined, template: this.floatingTemplate }
		const ownMatches = fuzzyFilter(query, [own], zone => zone.replace(/_/g, ' ')).length > 0

		return [
			...(query.trim().length === 0 ? [floating] : []),
			...(ownMatches ? [{ key: `zone:${own}`, value: own, template: this.zoneTemplate(own) }] : []),
			...zones.map(zone => ({ key: `zone:${zone}`, value: zone, template: this.zoneTemplate(zone) }))
		]
	}

	/** The chosen zone by its city alone ("Tehran (GMT+3:30)"): the face sits in a dense row, the list names it whole. */
	protected override renderValue(value: string): HTMLTemplateResult {
		// The core sends an unset (floating) zone as null.
		return value === null || value === undefined ? this.renderEmpty() : this.zoneTemplate(value, true)
	}

	/** An unset zone is a choice of its own — floating — not a bare placeholder. */
	protected override renderEmpty(): HTMLTemplateResult {
		return this.floatingTemplate
	}

	private get floatingTemplate() {
		return html`<p7t-icon-item small icon='lucide:clock'>Floating</p7t-icon-item>`
	}

	private zoneTemplate(zone: string, cityOnly = false) {
		const offset = offsetOf(zone)
		const name = cityOnly ? zoneCity(zone) : zone.replace(/_/g, ' ')
		return html`<p7t-icon-item small icon='lucide:globe' title=${zone}>${name}${offset ? ` (${offset})` : ''}</p7t-icon-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-time-zone-select': TimeZoneSelect
	}
}
