import { Component, component, css, html, property, type PropertyValues } from '@a11d/lit'
import { IconName, tooltip } from '..'
import { entityKindOf, isInactiveEntity, type EntityKind } from '../grid/entityActions'
import type { MoonlightNode } from './moonlightTree'

/** One kind of thing a box holds, counted. */
interface MoonlightTally {
	readonly icon: IconName
	readonly count: number
	readonly label: string
}

/** The kinds a box tallies, in the order it lists them, with their singular and plural names. */
const tallied: readonly { kind: EntityKind, icon: IconName, one: string, many: string }[] = [
	{ kind: 'lunar-directive', icon: 'directive-lunar', one: 'lunar directive', many: 'lunar directives' },
	{ kind: 'objective', icon: 'objective', one: 'objective', many: 'objectives' },
	{ kind: 'fate', icon: 'fate', one: 'fate', many: 'fates' },
	{ kind: 'decree', icon: 'decree', one: 'decree', many: 'decrees' }
]

/**
 * A lunar directive nested two or more levels under a Moonlight card, drawn as a card-like box two tiles tall.
 *
 * A section lays out one level only, so what sits deeper is summarised here rather than drawn: the box heads its
 * directive (editable, with its full banner and add actions a click away) and tallies what the directive holds.
 */
@component('p7t-moonlight-directive-box')
export class MoonlightDirectiveBox extends Component {
	@property({ type: Object }) node?: MoonlightNode

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				justify-content: space-between;
				gap: .5em;
				min-width: 0;
				box-sizing: border-box;
				padding: .6em .5em .55em .6em;
				border-radius: 12px;
				border: 1px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 24%, transparent);
				background: linear-gradient(
					160deg,
					color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 13%, var(--background-primary)),
					var(--background-primary) 75%);
				transition: border-color .3s ease;
			}

			:host(:hover) {
				border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 50%, transparent);
			}

			.tallies {
				display: flex;
				flex-wrap: wrap;
				align-items: center;
				gap: .3em .9em;
				padding-inline-start: .15em;
				color: var(--text-muted);
				font-family: var(--font-interface);
				font-size: .85em;
			}

			.tally {
				display: inline-flex;
				align-items: center;
				gap: .3em;

				& p7t-icon {
					width: 1.15em;
					height: 1.15em;
				}
			}

			.vacant {
				opacity: .6;
				font-style: italic;
			}

			/* A stale directive's box is dimmed; its heading dims itself, its actions keep their strength. */
			:host([inactive]) .tallies {
				opacity: .4;
			}
		`
	}

	protected override updated(changed: PropertyValues<this>) {
		super.updated(changed)
		this.toggleAttribute('inactive', isInactiveEntity(this.node?.directive))
	}

	/** What the directive holds, one entry per kind present. */
	private get tallies(): MoonlightTally[] {
		const node = this.node
		if (!node) {
			return []
		}

		const counts = new Map<EntityKind, number>([['lunar-directive', node.subdirectives.length]])
		for (const incentive of node.incentives) {
			const kind = entityKindOf(incentive)
			counts.set(kind, (counts.get(kind) ?? 0) + 1)
		}

		return tallied
			.map(({ kind, icon, one, many }) => {
				const count = counts.get(kind) ?? 0
				return { icon, count, label: `${count} ${count === 1 ? one : many}` }
			})
			.filter(tally => tally.count > 0)
	}

	protected override get template() {
		const node = this.node
		if (!node) {
			return html``
		}

		const tallies = this.tallies
		return html`
			<p7t-moonlight-heading level='box' addable .directive=${node.directive}></p7t-moonlight-heading>
			<div class='tallies'>
				${tallies.length === 0 ? html`<span class='vacant'>Nothing beneath yet</span>` : tallies.map(tally => html`
					<span class='tally' ${tooltip(tally.label)}>
						<p7t-icon icon=${tally.icon}></p7t-icon>
						${tally.count}
					</span>
				`)}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-directive-box': MoonlightDirectiveBox
	}
}
