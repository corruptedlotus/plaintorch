import { component, css, html, state } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { DependencyEndpointKind, Objective, ObjectiveStatus, OnrushSprint } from "@pleiades/sdk"
import { core, DerivedRef, isInactiveStatus, shiftState, tooltip, type Status } from '..'
import { dependencyRoute } from '../canvas/graphModel'

/**
 * An objective in a list. Its notch is its workflow state — or, while an unmet dependency holds its next transition
 * back (PEP101), the blocker in the state's place: the blocked glyph for an objective yet to start (standby, blocked,
 * onrush), the raced glyph for one in Polaris yet to finish, drawn in the flare accent. Which gate concerns the next
 * transition is the core's call (`gatesNextTransition`); the item reads the shared dependency listing for it, so it
 * follows every change the listing hears of.
 */
@component('p7t-objective-item')
export class ObjectiveItem extends EntityItem<Objective> {

	@state() currentOnrush?: OnrushSprint

	private readonly dependencies = new DerivedRef(this, core.repos.dependencyList)

	protected get objective() {
		return this.entity
	}

	static override get styles() {
		return css`
			${super.styles}

			.college {
				width: 36px;
				align-self: stretch;
				display: flex;
				align-items: center;
				justify-content: center;
			}

			/* The blocker glyph stands where the state glyph would, at the chip's size, in the flare accent. */
			.blocker {
				width: 1.8em;
				height: 1.8em;
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	/**
	 * The blocker on the objective's next transition, if anything holds it: `start` while it has not begun, `finish`
	 * while it is in Polaris; none for a finished objective or an unblocked one.
	 */
	private get nextTransitionBlocker(): 'start' | 'finish' | undefined {
		const objective = this.objective
		if (!objective || objective.status >= ObjectiveStatus.Done) {
			return undefined
		}

		const route = dependencyRoute(this.dependencies.value ?? [], { kind: DependencyEndpointKind.Objective, id: objective.id })
		if (!route.nextBlocked) {
			return undefined
		}

		return objective.status >= ObjectiveStatus.Polaris ? 'finish' : 'start'
	}

	/** An objective done with — done, archived or failed — is drawn dimmed. */
	override get disabled() {
		return this.entity !== undefined && isInactiveStatus(ObjectiveStatus[this.entity.status] as Status)
	}

	/**
	 * The notch shifts the objective's state through the state bubble, which saves it and shows what became of it. The
	 * answer is absorbed into the canonical instance, so every surface showing the objective follows.
	 */
	protected override async notchAction(notch: HTMLElement) {
		if (this.objective) {
			await shiftState(notch, this.objective)
		}
	}

	protected override get preTitle() {
		// The chip carries the directive glyph, the title, the "World Quest" placeholder, and the mini-banner tooltip.
		return html`<p7t-directive-item small .directive=${this.objective!.directive}></p7t-directive-item>`
	}

	protected override get info() {
		return html`<p7t-celestron-item ?starfire=${this.currentOnrush && (this.currentOnrush.id === this.objective?.onrushSprintId)} small .value=${this.objective!.celestronValue}></p7t-celestron-item>`
	}

	protected override get notchTemplate() {
		const blocker = this.nextTransitionBlocker
		if (blocker) {
			return html`
				<p7t-icon class='blocker'
					icon=${blocker === 'start' ? 'state-blocked' : 'state-raced'}
					${tooltip(blocker === 'start' ? 'Blocked by Dependencies' : 'Progress Blocked')}>
				</p7t-icon>
			`
		}

		return html`
			<p7t-status-item icon-only .status=${ObjectiveStatus[this.objective!.status] as keyof typeof ObjectiveStatus}></p7t-status-item>
		`
	}

	protected override get highlightInfo() {
		return html`
			<div class='college'>
				<p7t-college-item mode='icon' .college=${this.objective!.college}></p7t-college-item>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item': ObjectiveItem
	}
}
