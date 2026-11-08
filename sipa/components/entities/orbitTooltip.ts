import { html, nothing, type HTMLTemplateResult } from '@a11d/lit'
import { humanizeOrbit, type OrbitCalendarName } from '../../orbits'

/**
 * The tooltip an orbit carries wherever a schedule is shown: its full humanised reading (never truncated) over the raw
 * notation it stands for, read on the calendar the orbit resolves on. The schedule chip tips its face with it, and an
 * occurrence born of the orbit tips its "Orbit" source with it, so the two say the same thing.
 */
export function orbitTooltip(orbit: string, calendar: OrbitCalendarName): HTMLTemplateResult {
	const { text, invalid } = humanizeOrbit(orbit, false, calendar)
	return html`
		<style>
			.schedule-tip .tip-phrase { font-weight: 400; }
			.schedule-tip .tip-raw {
				display: block;
				margin-block-start: .35em;
				font-family: var(--font-monospace);
				font-size: .9em;
				opacity: .6;
			}
		</style>
		<div class='schedule-tip'>
			${!text || invalid ? nothing : html`<div class='tip-phrase'>${text}</div>`}
			<code class='tip-raw'>${orbit}</code>
		</div>
	`
}
