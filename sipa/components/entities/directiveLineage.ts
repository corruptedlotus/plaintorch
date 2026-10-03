import type { Directive } from '@pleiades/sdk'

/**
 * The directives from one up to the root of its hierarchy, that directive first — the chain a breadcrumb draws, the
 * ancestry a picker row chips, the lineage a `directive:` filter matches against.
 *
 * Resolved against a loaded listing by id rather than a `parentDirective` nav, since every directive carries its
 * `parentDirectiveId` scalar and the whole hierarchy is in the store. A hand-edited vault can close a parent chain into
 * a loop, so the walk remembers where it has been rather than trusting a chain to end; a parent the listing does not
 * hold ends the chain there. No start, or one the listing lacks, is an empty chain.
 */
export function directiveChain(startId: string | null | undefined, directives: ReadonlyMap<string, Directive> | readonly Directive[]): Directive[] {
	const byId = directives instanceof Map ? directives : new Map((directives as readonly Directive[]).map(directive => [directive.id, directive]))
	const chain: Directive[] = []
	const seen = new Set<string>()
	let id: string | null | undefined = startId
	while (id && !seen.has(id)) {
		seen.add(id)
		const directive = byId.get(id)
		if (!directive) {
			break
		}

		chain.push(directive)
		id = directive.parentDirectiveId
	}

	return chain
}

/** The directives above one, nearest first — its chain without itself. */
export function directiveAncestors(directive: Directive, directives: ReadonlyMap<string, Directive> | readonly Directive[]): Directive[] {
	return directiveChain(directive.parentDirectiveId, directives).filter(ancestor => ancestor.id !== directive.id)
}
