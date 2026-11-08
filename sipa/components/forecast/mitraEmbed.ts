/**
 * The date span a Mitra day view covers — what its `range-change` event carries and `range` answers: the days it
 * renders, which reach past the ones in sight so a scroll shows data at once.
 */
export interface MitraRange {
	/** Local midnight of the first day. */
	readonly start: Date
	/** Local midnight after the last day. */
	readonly end: Date
	/** Local midnight of every day, in order. */
	readonly days: readonly Date[]
}

/** A host hook producing its own element for one day of a Mitra day view (given that day's local midnight). */
export type MitraDaySlot = (date: Date) => Node | string | null

/**
 * The slice of `<mitra-embed>` — Mitra's embeddable calendar view (vendored, see `vendor/mitra`) — that SIPA drives.
 * The element renders a registered Mitra view fed by the host rather than by Mitra's backend; PLAINTORCH feeds it no
 * calendar entries and fills its days with its own elements instead.
 */
export interface MitraEmbedElement extends HTMLElement {
	/** The registered view to render — `week-all-day`, a strip of day columns. */
	view: string
	/** The date the view frames: with a {@link dayCount}, the first day in sight. */
	date: Date | string | number
	/** How many day columns fit the width; the strip scrolls sideways through the days beyond. */
	dayCount?: number
	/** Fills each day below its header — the host element stretched over the rest of the column. */
	dayFill?: MitraDaySlot
	/** A fixed band of host content under each day's header. */
	dayContent?: MitraDaySlot
	/** The span the view covers now (see {@link MitraRange}). */
	readonly range: MitraRange
	/**
	 * Re-runs the day hooks for every rendered day. The view keeps what a hook returned per day; this is for when the
	 * host's answer for a day changes, not for an element that updates itself.
	 */
	refreshDayContent(): void
}

let loading: Promise<void> | undefined

/**
 * Loads Mitra's embed bundle, once, defining `<mitra-embed>`. Deferred to the first surface that needs it: the bundle
 * is large and installs globals of its own (see `vendor/mitra/README.md`), so nothing else pays for it.
 */
export function loadMitraEmbed(): Promise<void> {
	return loading ??= import('../../vendor/mitra/embed.js').then(() => undefined)
}

declare global {
	interface HTMLElementTagNameMap {
		'mitra-embed': MitraEmbedElement
	}

	interface HTMLElementEventMap {
		'range-change': CustomEvent<MitraRange>
	}
}
