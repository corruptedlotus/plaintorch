import type { DirectiveTimeframeRecord } from '@pleiades/sdk'

/** What a directive is named by: its title, and the codename it may carry. */
export interface DirectiveNaming {
	title: string
	codename?: string | null | undefined
}

/**
 * The text a directive is found by, wherever one is searched: its title and its codename. Every picker and search that
 * matches directives — or things named after their directive — builds its haystack from this, so "ALF" finds the
 * directive codenamed ALF in the backlog, the move picker and the timeframe pickers alike, and the core's own directive
 * search folds the same two fields.
 */
export function directiveSearchText(directive: DirectiveNaming): string {
	return directive.codename ? `${directive.title} ${directive.codename}` : directive.title
}

/** The text a timeframe is found by: its own title, then its owning directive's title and codename. */
export function timeframeSearchText(timeframe: DirectiveTimeframeRecord): string {
	return `${timeframe.title} ${directiveSearchText({ title: timeframe.directiveTitle, codename: timeframe.directiveCodename })}`
}
