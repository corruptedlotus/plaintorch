import { component, css, property } from "@a11d/lit"
import { OrbitUnit } from "@pleiades/sdk"
import { formatDuration, type DurationPart, type TimeUnit } from "../../orbits"
import { EditableTextPart } from "./EditableTextPart"
import { granularityDescriptors } from "./GranularitySelect"

/** One or more `<count><unit>` parts and nothing else — the Orbit duration notation an Epoch stores. */
const durationNotation = /^(\d+[yMwdhms])+$/

/** The parts of well-formed Orbit duration notation, or `undefined` when it is not (a zero count included). */
export function parseDuration(notation: string): DurationPart[] | undefined {
	if (!durationNotation.test(notation)) {
		return undefined
	}

	const parts = [...notation.matchAll(/(\d+)([yMwdhms])/g)].map(([, count, unit]) => ({ count: Number(count), unit: unit as TimeUnit }))
	return parts.every(part => part.count > 0) ? parts : undefined
}

/**
 * An editable nominal duration — an Epoch's `duration` — in Orbit duration notation: `90m`, `1d6h`, `1M`. Idle it reads
 * in words ("1 day and 6 hours", as a schedule's span does); editing shows the raw notation. Notation that does not
 * parse warns and reverts; clearing it leaves no duration, which fills exactly one unit of the occurrence's
 * {@link granularity} — so the empty field says so ("Whole day").
 */
@component('p7t-editable-duration')
export class EditableDuration extends EditableTextPart<string> {
	/** The granularity the duration belongs to — what "no duration" fills, one whole unit of. */
	@property({ type: Number }) granularity?: OrbitUnit

	override label = 'Duration'

	static override get styles() {
		return css`
			${super.styles}

			:host {
				font-weight: 300;
			}

			/* Raw notation reads better in a mono face while editing. */
			:host([active]) {
				font-family: var(--font-monospace);
			}
		`
	}

	protected override toDisplayText(value: string | undefined): string {
		const parts = value ? parseDuration(value) : undefined
		if (parts) {
			return formatDuration(parts)
		}

		const unit = this.granularity === undefined ? undefined : granularityDescriptors[this.granularity]
		return unit ? `Whole ${unit.label.toLowerCase()}` : 'Whole window'
	}

	protected override toEditableText(value: string | undefined): string {
		return value ?? ''
	}

	protected override parseValue(raw: string): string | undefined {
		const trimmed = raw.trim()
		return trimmed.length > 0 ? trimmed : undefined
	}

	protected override validateContent(trimmed: string): string | undefined {
		return trimmed.length === 0 || parseDuration(trimmed) ? undefined : 'Use Orbit duration notation, such as 90m, 1d6h or 1M.'
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-duration': EditableDuration
	}
}
