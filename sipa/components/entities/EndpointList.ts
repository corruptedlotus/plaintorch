import { Component, component, css, html, nothing, property } from '@a11d/lit'
import { DependencyEndpointKind, type EndpointRef } from '@pleiades/sdk'
import { core, DerivedRef, IconName } from '..'
import { endpointKey } from '../canvas/graphModel'
import './IncentiveItem'
import './DirectiveItem'
import '../design/IconItem'

/** Which endpoints an {@link EndpointList} shows. Absent shows every kind; `'non-objective'` drops the objectives. */
export type EndpointFilter = 'non-objective'

/** The glyph each kind falls back to when its entity has not resolved — matching the grid, banners and canvas nodes. */
const kindIcons: Record<DependencyEndpointKind, IconName> = {
	[DependencyEndpointKind.Directive]: 'directive',
	[DependencyEndpointKind.Objective]: 'objective',
	[DependencyEndpointKind.Fate]: 'fate',
	[DependencyEndpointKind.Eventive]: 'eventive',
	[DependencyEndpointKind.Checkpoint]: 'checkpoint'
}

/**
 * A list of dependency endpoints, each drawn as the compact chip its kind wants — an incentive as a
 * {@link IncentiveItem}, a directive as a {@link DirectiveItem}, a checkpoint or an unresolved endpoint as a plain
 * glyph-and-title chip, and an eventive as its owning incentive under the occurrence's own date and time.
 *
 * Reusable wherever a set of endpoints wants showing: the {@link filter} narrows what is drawn without the caller
 * having to pre-sieve them, so the same list handed the whole set can show everything one place and only the
 * non-objective ones another (the onrush card's count tooltip, where the objectives already list as rows). The
 * entities behind the endpoints are resolved from the shared listings, so a name appears as soon as its listing
 * is loaded and an id stands in until then.
 *
 * An endpoint whose block does not concern the next lifecycle transition — a finish gate on something not yet begun,
 * a link further down a chain that is not holding anything back right now — can be marked {@link nonImmediate}: it
 * is drawn faint and listed after the rest, so what is in the way *now* reads first.
 */
@component('p7t-endpoint-list')
export class EndpointList extends Component {
	@property({ attribute: false }) endpoints: readonly EndpointRef[] = []
	@property() filter?: EndpointFilter

	/** Keys (`endpointKey`) of the endpoints whose block does not concern the next lifecycle transition, drawn faint. */
	@property({ attribute: false }) nonImmediate: ReadonlySet<string> = new Set()

	private readonly objectiveList = new DerivedRef(this, core.repos.objectiveList)
	private readonly fateList = new DerivedRef(this, core.repos.fateList)
	private readonly directiveList = new DerivedRef(this, core.repos.directiveList)
	private readonly checkpointList = new DerivedRef(this, core.repos.checkpointList)

	static override get styles() {
		return css`
			:host {
				display: block;
			}

			.heading {
				font-weight: bold;
				color: var(--text-muted);
				text-transform: uppercase;
				font-size: .8em;
				margin-bottom: .5em;
			}

			.list {
				display: flex;
				flex-direction: column;
				gap: .35em;
			}

			/* In the way, but not of the next transition. */
			.distant {
				opacity: .4;
			}

			.endpoint {
				display: flex;
				flex-direction: column;
				gap: .1em;
			}

			/* The eventive's occurrence, sitting under its owner and indented past the glyph. */
			.occurrence {
				font-size: .8em;
				opacity: .6;
				margin-inline-start: 1.6em;
				line-height: 1;
			}
		`
	}

	/** The endpoints to draw, after the {@link filter}: the immediate ones first, the {@link nonImmediate} ones after. */
	private get shown(): readonly EndpointRef[] {
		const filtered = this.filter === 'non-objective'
			? this.endpoints.filter(endpoint => endpoint.kind !== DependencyEndpointKind.Objective)
			: this.endpoints
		return [...filtered].sort((a, b) => Number(this.isDistant(a)) - Number(this.isDistant(b)))
	}

	private isDistant(endpoint: EndpointRef): boolean {
		return this.nonImmediate.has(endpointKey(endpoint))
	}

	protected override get template() {
		const shown = this.shown
		if (shown.length === 0) {
			return nothing
		}

		return html`
			<div class='heading'><slot></slot></div>
			<div class='list'>${shown.map(endpoint => html`<div class='${this.isDistant(endpoint) ? 'distant' : ''}'>${this.endpointTemplate(endpoint)}</div>`)}</div>
		`
	}

	private endpointTemplate(endpoint: EndpointRef) {
		switch (endpoint.kind) {
			case DependencyEndpointKind.Objective:
			case DependencyEndpointKind.Fate: {
				const incentive = this.incentiveFor(endpoint)
				return incentive
					? html`<p7t-incentive-item small .incentive=${incentive}></p7t-incentive-item>`
					: this.fallback(endpoint)
			}
			case DependencyEndpointKind.Directive: {
				const directive = this.directiveList.value?.find(candidate => candidate.id === endpoint.id)
				return directive
					? html`<p7t-directive-item small .directive=${directive}></p7t-directive-item>`
					: this.fallback(endpoint)
			}
			case DependencyEndpointKind.Checkpoint: {
				const checkpoint = this.checkpointList.value?.find(candidate => candidate.id === endpoint.id)
				return html`<p7t-icon-item small icon='checkpoint' .text=${checkpoint?.title ?? endpoint.id}></p7t-icon-item>`
			}
			case DependencyEndpointKind.Eventive:
				return this.eventiveTemplate(endpoint)
			default:
				return this.fallback(endpoint)
		}
	}

	/** An objective or fate endpoint, resolved to its entity for the incentive chip. */
	private incentiveFor(endpoint: EndpointRef) {
		return endpoint.kind === DependencyEndpointKind.Fate
			? this.fateList.value?.find(fate => fate.id === endpoint.id)
			: this.objectiveList.value?.find(objective => objective.id === endpoint.id)
	}

	/**
	 * An eventive endpoint: its owning incentive (a fate or an objective, addressed by the eventive's owner id)
	 * under a subtle line for the occurrence the endpoint pins, read from its own recurrence slot.
	 */
	private eventiveTemplate(endpoint: EndpointRef) {
		const owner = this.fateList.value?.find(fate => fate.id === endpoint.id)
			?? this.objectiveList.value?.find(objective => objective.id === endpoint.id)
		const when = occurrenceLabel(endpoint)
		return html`
			<div class='endpoint'>
				${owner
					? html`<p7t-incentive-item small .incentive=${owner}></p7t-incentive-item>`
					: html`<p7t-icon-item small icon='eventive' .text=${endpoint.id}></p7t-icon-item>`}
				${when ? html`<span class='occurrence'>${when}</span>` : nothing}
			</div>
		`
	}

	/** A plain kind-glyph chip labelled by id, for a checkpoint-less kind or an endpoint whose entity has not resolved. */
	private fallback(endpoint: EndpointRef) {
		return html`<p7t-icon-item small .icon=${kindIcons[endpoint.kind]} .text=${endpoint.id}></p7t-icon-item>`
	}
}

/**
 * The occurrence an eventive endpoint pins, as a short 'Mon 12 · 14:30', from its recurrence slot. The slot is a
 * civil ISO datetime; a midnight slot is an all-day occurrence and shows its day alone.
 */
function occurrenceLabel(endpoint: EndpointRef): string | undefined {
	if (!endpoint.recurrenceId) {
		return undefined
	}

	const day = formatDate(endpoint.recurrenceId.slice(0, 10))
	const time = endpoint.recurrenceId.slice(11, 16)
	return time && time !== '00:00' ? `${day} · ${time}` : day
}

/** 'YYYY-MM-DD' → a short 'Mon 12' label, parsed as local time so the day never shifts across a timezone. */
function formatDate(date: string): string {
	const parsed = new Date(`${date}T00:00:00`)
	if (Number.isNaN(parsed.getTime())) {
		return date
	}

	return parsed.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' })
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-endpoint-list': EndpointList
	}
}
