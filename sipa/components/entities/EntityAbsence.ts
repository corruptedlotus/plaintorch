import { Component, component, css, event, html, nothing, property } from '@a11d/lit'

/**
 * Why a surface has no entity to show: the core answered that there is none, or it could not be asked.
 */
export type EntityAbsenceState = 'removed' | 'unavailable'

/**
 * What a surface shows in place of an entity it resolves but cannot show.
 *
 * Two different things used to look the same — a blank surface. `removed` says the core has no such entity: it
 * was deleted while the surface was open, or the PUCK never named one. `unavailable` says the entity could not
 * be loaded, which is a statement about the connection rather than about the entity, so it offers to try again
 * and raises {@link retry} when asked to.
 */
@component('p7t-entity-absence')
export class EntityAbsence extends Component {
	@property() state: EntityAbsenceState = 'removed'
	/** The PUCK the surface was asked to show, named so the notice says what is missing. */
	@property() puck = ''

	/** Raised when the user asks for another attempt at loading an unavailable entity. */
	@event() readonly retry!: EventDispatcher

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
				gap: .8em;
				padding: .8em .6em;
				font-family: var(--font-interface);
				font-weight: 300;
				color: var(--text-muted);
			}

			p7t-icon {
				flex: 0 0 1.6em;
				width: 1.6em;
				height: 1.6em;
				opacity: .7;
			}

			.message {
				flex: 1;
				display: flex;
				flex-direction: column;
				gap: .2em;
			}

			.puck {
				font-family: var(--font-monospace);
				font-size: .75em;
				opacity: .6;
			}
		`
	}

	protected override get template() {
		const removed = this.state === 'removed'
		return html`
			<p7t-icon icon=${removed ? 'lucide:circle-off' : 'lucide:triangle-alert'}></p7t-icon>
			<div class='message'>
				<span>${removed ? 'This entity no longer exists.' : 'PLAINTORCH could not load this entity.'}</span>
				${!this.puck ? nothing : html`<span class='puck'>${this.puck}</span>`}
			</div>
			${removed ? nothing : html`
				<p7t-button ghost icon='lucide:rotate-ccw' @click=${() => this.retry.dispatch()}><span>Try again</span></p7t-button>
			`}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-absence': EntityAbsence
	}
}
