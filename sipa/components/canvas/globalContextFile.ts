import type { EndpointHit } from '@pleiades/sdk'

/** The vault extension a saved global planning context lives under (PEP102). */
export const GLOBAL_CONTEXT_EXTENSION = 'p7tpx'

/**
 * The on-disk shape of a `.p7tpx` file: the pinned set and the positions they were arranged into.
 *
 * A global context has no database home — it is either scratch or a file — so the file is the whole of it.
 * Each pin keeps its own title so the graph draws the instant the file loads, before anything is fetched, and
 * the layout is a plain key→point map (not a nested JSON string) so the file stays legible to a human editor.
 */
export interface GlobalContextFile {
	version: 1
	pinned: EndpointHit[]
	layout?: Record<string, { x: number, y: number }>
}

/**
 * Serialises a context to the file text. `layout` is the `serializePositions` blob the canvas hands over; it
 * is unpacked into an object so the file reads cleanly rather than embedding a quoted JSON string.
 */
export function serializeGlobalContext(pinned: readonly EndpointHit[], layout: string | undefined): string {
	let positions: Record<string, { x: number, y: number }> | undefined
	if (layout) {
		try {
			positions = JSON.parse(layout)
		}
		catch {
			positions = undefined
		}
	}

	const file: GlobalContextFile = { version: 1, pinned: [...pinned], layout: positions }
	return JSON.stringify(file, undefined, '\t')
}

/**
 * Reads a context from file text, forgivingly — a malformed or empty file yields an empty context rather than
 * throwing, since a broken layout should cost a re-placement, not a broken view. `layout` is handed back as
 * the string blob the canvas's `parsePositions` expects.
 */
export function parseGlobalContext(text: string): { pinned: EndpointHit[], layout: string | undefined } {
	if (!text.trim()) {
		return { pinned: [], layout: undefined }
	}

	let parsed: Partial<GlobalContextFile>
	try {
		parsed = JSON.parse(text) as Partial<GlobalContextFile>
	}
	catch {
		return { pinned: [], layout: undefined }
	}

	const pinned = Array.isArray(parsed.pinned) ? parsed.pinned.filter(isEndpointHit) : []
	const layout = parsed.layout && typeof parsed.layout === 'object' ? JSON.stringify(parsed.layout) : undefined
	return { pinned, layout }
}

function isEndpointHit(value: unknown): value is EndpointHit {
	const hit = value as EndpointHit
	return !!hit && typeof hit.id === 'string' && typeof hit.title === 'string' && typeof hit.kind === 'number'
}
